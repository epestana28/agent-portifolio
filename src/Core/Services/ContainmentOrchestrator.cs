using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Core.Services;

public sealed class ContainmentOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Longest Retry-After the worker will wait for. Beyond it the step is left visibly pending.</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>After this many deliveries the message is dead-lettered and the operation marked failed.</summary>
    public int MaxDeliveries { get; set; } = 5;
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
}

/// <summary>
/// External containment. Runs after the local block is already durable, so
/// nothing here is needed to stop new admissions. Its job is to act on the
/// Microsoft side and to report honestly, step by step, what was requested,
/// executed, verified, failed or is unknown. It never reactivates anything.
/// </summary>
public sealed class ContainmentOrchestrator(IWalletStore store, IEnumerable<IContainmentAdapter> adapters, TimeProvider time, ContainmentOptions options)
{
    public const string TokenLimitation =
        "Access tokens issued before this operation may stay valid until they expire. Continuous access evaluation is not guaranteed for this identity and resource.";

    private readonly IReadOnlyList<IContainmentAdapter> adapters = adapters.ToList();

    /// <summary>Drains the outbox once. Safe to call repeatedly and after redelivery.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var pending = await store.TransactAsync(tx => tx.ListPendingOutboxAsync(50), ct);
        var handled = 0;
        foreach (var message in pending.Where(m => m.Type == OutboxTypes.ContainmentRequested))
        {
            var delivery = await store.TransactAsync(async tx =>
            {
                var updated = message with { Deliveries = message.Deliveries + 1 };
                await tx.UpdateOutboxAsync(updated);
                return updated;
            }, ct);

            if (delivery.Deliveries > options.MaxDeliveries)
            {
                await DeadLetterAsync(delivery, ct);
            }
            else
            {
                await RunAsync(message.TenantId, message.SubjectId, ct);
                await store.TransactAsync(async tx =>
                {
                    await tx.UpdateOutboxAsync(delivery with { ProcessedAt = time.GetUtcNow() });
                    return true;
                }, ct);
            }

            handled++;
        }

        return handled;
    }

    /// <summary>Runs every applicable adapter for one operation. Steps already verified are not repeated.</summary>
    public async Task<ContainmentOperation> RunAsync(Guid tenantId, Guid operationId, CancellationToken ct)
    {
        var start = await store.TransactAsync(async tx =>
        {
            var operation = await tx.GetContainmentAsync(tenantId, operationId) ?? throw new NotFoundException("Containment operation not found.");
            var agent = await tx.GetAgentAsync(tenantId, operation.AgentId) ?? throw new NotFoundException("Agent not found.");

            // A message from an older epoch must not act on an agent that has since been
            // reviewed and reactivated. It is discarded, visibly.
            if (agent.State != AgentState.Suspended || agent.RevocationEpoch != operation.EpochAfter)
            {
                var superseded = operation with
                {
                    State = ContainmentState.Failed,
                    CompletedAt = time.GetUtcNow(),
                    Uncertainties = [.. operation.Uncertainties, "Superseded: the agent state or epoch changed before external containment ran. No external action was taken by this operation."],
                };
                await tx.UpdateContainmentAsync(superseded);
                await tx.AppendAuditAsync(SystemEvent(superseded, agent, EventTypes.ContainmentSuperseded, null));
                return (Operation: superseded, Agent: agent, Run: false);
            }

            if (operation.State is ContainmentState.LocalBlocked or ContainmentState.Requested)
            {
                operation = operation with { State = ContainmentState.ExternalPending };
                await tx.UpdateContainmentAsync(operation);
            }

            return (Operation: operation, Agent: agent, Run: true);
        }, ct);

        if (!start.Run)
        {
            return start.Operation;
        }

        var current = start.Operation;
        foreach (var adapter in adapters.Where(a => a.AppliesTo(start.Agent)))
        {
            var previous = current.Steps.FirstOrDefault(s => s.Name == adapter.Name);
            if (previous?.Status == StepStatus.Verified)
            {
                continue;
            }

            var step = await RunStepAsync(adapter, start.Agent, previous, ct);
            current = await SaveStepAsync(tenantId, operationId, start.Agent, step, ct);
        }

        return await CompleteAsync(tenantId, operationId, start.Agent, ct);
    }

    /// <summary>
    /// Resolves steps left as Unknown, Executed or Requested by probing again.
    /// Only the independent probe can move a step to Verified.
    /// </summary>
    public async Task<ContainmentOperation> ReconcileAsync(Guid tenantId, Guid operationId, CancellationToken ct)
    {
        var (operation, agent) = await store.TransactAsync(async tx =>
        {
            var o = await tx.GetContainmentAsync(tenantId, operationId) ?? throw new NotFoundException("Containment operation not found.");
            var a = await tx.GetAgentAsync(tenantId, o.AgentId) ?? throw new NotFoundException("Agent not found.");
            return (o, a);
        }, ct);

        foreach (var step in operation.Steps.Where(s => s.Status != StepStatus.Verified))
        {
            var adapter = adapters.FirstOrDefault(a => a.Name == step.Name);
            if (adapter is null)
            {
                continue;
            }

            var probe = await ProbeAsync(adapter, agent, ct);
            if (probe.Verified)
            {
                await SaveStepAsync(tenantId, operationId, agent, step with
                {
                    Status = StepStatus.Verified,
                    LastCheckedAt = time.GetUtcNow(),
                    Evidence = probe.Evidence,
                    Limitation = adapter.StandingLimitation,
                }, ct);
            }
        }

        return await CompleteAsync(tenantId, operationId, agent, ct);
    }

    private async Task<ContainmentStep> RunStepAsync(IContainmentAdapter adapter, Agent agent, ContainmentStep? previous, CancellationToken ct)
    {
        var startedAt = previous?.StartedAt ?? time.GetUtcNow();
        var attempts = previous?.Attempts ?? 0;
        var resource = adapter.DescribeResource(agent);

        ContainmentStep Step(StepStatus status, string? limitation, string? evidence) =>
            new(adapter.Name, resource, status, limitation, attempts, startedAt, time.GetUtcNow(), evidence);

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            attempts++;
            AdapterResult result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(options.AttemptTimeout);
                try
                {
                    result = await adapter.ExecuteAsync(agent, timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The request may have been applied. Do not guess, and do not blindly repeat.
                    return Step(StepStatus.Unknown, "Timed out: the external result is unknown. Reconcile by probing before trusting this step.", null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = new AdapterResult(AdapterOutcome.Failed, ex.GetType().Name);
                }
            }

            if (result.Outcome == AdapterOutcome.Done)
            {
                var probe = await ProbeAsync(adapter, agent, ct);
                return probe.Verified
                    ? Step(StepStatus.Verified, adapter.StandingLimitation, probe.Evidence)
                    : Step(StepStatus.Executed, "Executed according to the administrative response, but not confirmed by an independent probe.", result.Evidence);
            }

            if (result.Outcome == AdapterOutcome.Throttled)
            {
                var wait = result.RetryAfter ?? Backoff(attempt);
                if (wait > options.MaxRetryAfter || attempt == options.MaxAttempts)
                {
                    return Step(StepStatus.Requested, $"Throttled by the external service (retry after {wait.TotalSeconds:0}s). Not executed yet.", result.Evidence);
                }

                await options.Delay(wait, ct);
                continue;
            }

            if (attempt == options.MaxAttempts)
            {
                return Step(StepStatus.Failed, "Failed after the maximum number of attempts.", result.Evidence);
            }

            await options.Delay(Backoff(attempt), ct);
        }

        return Step(StepStatus.Failed, "Failed after the maximum number of attempts.", null);
    }

    private async Task<(bool Verified, string? Evidence)> ProbeAsync(IContainmentAdapter adapter, Agent agent, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.AttemptTimeout);
        try
        {
            var result = await adapter.VerifyAsync(agent, timeout.Token);
            return (result.Outcome == AdapterOutcome.Done, result.Evidence);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (false, null);
        }
    }

    private Task<ContainmentOperation> SaveStepAsync(Guid tenantId, Guid operationId, Agent agent, ContainmentStep step, CancellationToken ct) =>
        store.TransactAsync(async tx =>
        {
            var operation = await tx.GetContainmentAsync(tenantId, operationId) ?? throw new NotFoundException("Containment operation not found.");
            var steps = operation.Steps.Where(s => s.Name != step.Name).Append(step).ToList();
            operation = operation with { Steps = steps };
            await tx.UpdateContainmentAsync(operation);
            await tx.AppendAuditAsync(SystemEvent(operation, agent, EventTypes.ContainmentStep, step));
            return operation;
        }, ct);

    private Task<ContainmentOperation> CompleteAsync(Guid tenantId, Guid operationId, Agent agent, CancellationToken ct) =>
        store.TransactAsync(async tx =>
        {
            var operation = await tx.GetContainmentAsync(tenantId, operationId) ?? throw new NotFoundException("Containment operation not found.");
            var state = Summarise(operation.Steps);
            var verified = operation.Steps.Where(s => s.Status == StepStatus.Verified).Select(s => $"{s.Name} on {s.Resource}").ToList();
            var scope = verified.Count == 0
                ? "local admission only"
                : "local admission; verified externally: " + string.Join("; ", verified);
            var uncertainties = operation.Uncertainties
                .Concat(operation.Steps.Where(s => s.Limitation is not null).Select(s => $"{s.Name}: {s.Limitation}"))
                .Append(TokenLimitation)
                .Distinct()
                .ToList();

            operation = operation with { State = state, Scope = scope, CompletedAt = time.GetUtcNow(), Uncertainties = uncertainties };
            await tx.UpdateContainmentAsync(operation);
            await tx.AppendAuditAsync(SystemEvent(operation, agent, EventTypes.ContainmentCompleted, null));
            return operation;
        }, ct);

    /// <summary>
    /// The most pessimistic reading wins. "Verified" needs every step confirmed by
    /// probe, and even then it is scoped to the listed resources only.
    /// </summary>
    public static ContainmentState Summarise(IReadOnlyList<ContainmentStep> steps)
    {
        if (steps.Count == 0)
        {
            return ContainmentState.LocalBlocked;
        }

        if (steps.All(s => s.Status == StepStatus.Verified))
        {
            return ContainmentState.VerifiedScoped;
        }

        if (steps.Any(s => s.Status == StepStatus.Unknown))
        {
            return ContainmentState.Unknown;
        }

        return steps.All(s => s.Status == StepStatus.Failed) ? ContainmentState.Failed : ContainmentState.Partial;
    }

    private Task DeadLetterAsync(OutboxMessage message, CancellationToken ct) =>
        store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            await tx.UpdateOutboxAsync(message with { ProcessedAt = now, DeadLettered = true });
            var operation = await tx.GetContainmentAsync(message.TenantId, message.SubjectId);
            if (operation is not null)
            {
                operation = operation with
                {
                    State = operation.State == ContainmentState.VerifiedScoped ? operation.State : ContainmentState.Failed,
                    Uncertainties = [.. operation.Uncertainties, "Dead-lettered: external containment did not complete automatically. Follow the break-glass runbook."],
                };
                await tx.UpdateContainmentAsync(operation);
                await tx.AppendAuditAsync(Audit.Create(now, operation.TenantId, operation.AgentId, EventTypes.ContainmentDeadLettered,
                    $"containment:{operation.Id}", AccessModes.System, revocationEpoch: operation.EpochAfter,
                    executionState: operation.State.ToString(), attempt: message.Deliveries, evidenceRef: $"containment:{operation.Id}"));
            }

            return true;
        }, ct);

    private TimeSpan Backoff(int attempt) => TimeSpan.FromTicks(options.BaseBackoff.Ticks * (1L << (attempt - 1)));

    private AuditEvent SystemEvent(ContainmentOperation operation, Agent agent, string eventType, ContainmentStep? step) =>
        Audit.Create(time.GetUtcNow(), operation.TenantId, operation.AgentId, eventType, $"containment:{operation.Id}", AccessModes.System,
            humanInitiator: operation.InitiatedBy, policyVersion: agent.PolicyVersion, revocationEpoch: operation.EpochAfter,
            resource: step?.Resource, operation: step?.Name, executionState: step?.Status.ToString() ?? operation.State.ToString(),
            attempt: step?.Attempts, evidenceRef: $"containment:{operation.Id}");
}
