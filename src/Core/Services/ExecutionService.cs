using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Core.Services;

public enum ExecutionStatus { Succeeded, Denied, Cancelled, Failed, Unknown }

public sealed record ExecutionOutcome(ExecutionStatus Status, string ReasonCode, Guid? ActionId, object? Payload, bool Replayed = false);

public sealed class ExecutionOptions
{
    /// <summary>After this the external result is recorded as unknown, never as success or failure.</summary>
    public TimeSpan ExternalTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// The admission pipeline in front of every protected action:
/// resolve identity, decide and persist the intent, revalidate, execute one
/// allowlisted operation, persist the result. Fails closed whenever the
/// authoritative state cannot be read or written.
/// </summary>
public sealed class ExecutionService(IWalletStore store, IDocumentGateway gateway, TimeProvider time, ExecutionOptions options)
{
    public async Task<ExecutionOutcome> ExecuteAsync(CallerIdentity caller, ExecutionRequest request, string correlationId, CancellationToken cancellationToken)
    {
        var resolvedOk = OperationCatalog.TryResolve(request, out var operation, out var invalidReason);

        Admission admission;
        try
        {
            admission = await store.TransactAsync(tx => AdmitAsync(tx, caller, request, resolvedOk ? operation : null, invalidReason, correlationId), cancellationToken);
        }
        catch (StoreUnavailableException)
        {
            // No authoritative state, no durable intent: deny. There is no positive cache to fall back on.
            return new ExecutionOutcome(ExecutionStatus.Denied, ReasonCodes.StateUnavailable, null, null);
        }

        if (admission.Outcome is not null)
        {
            return admission.Outcome;
        }

        var action = admission.Action!;
        string? cancelReason;
        try
        {
            cancelReason = await store.TransactAsync(tx => RevalidateAsync(tx, caller, action, correlationId), cancellationToken);
        }
        catch (StoreUnavailableException)
        {
            // The action stays "Admitted" with no effect; the stale sweep will close it.
            return new ExecutionOutcome(ExecutionStatus.Denied, ReasonCodes.StateUnavailable, action.Id, null);
        }

        if (cancelReason is not null)
        {
            return new ExecutionOutcome(ExecutionStatus.Cancelled, cancelReason, action.Id, null);
        }

        // From here on a suspension can no longer stop this action: the request is on
        // its way to the external service. The containment report lists it as in flight.
        var (state, reason, payload) = await CallExternalAsync(admission.Agent!, operation, cancellationToken);

        try
        {
            await store.TransactAsync(async tx =>
            {
                var now = time.GetUtcNow();
                await tx.UpdateActionAsync(action with { State = state, CompletedAt = now, ReasonCode = reason });
                await tx.AppendAuditAsync(Audit.Create(now, action.TenantId, action.AgentId, EventTypes.ActionCompleted, correlationId,
                    AccessModes.Application, callerIdentity: caller.ObjectId, executorIdentity: admission.Agent!.ExecutorObjectId,
                    policyVersion: action.PolicyVersion, revocationEpoch: action.RevocationEpoch, capabilityId: action.CapabilityId,
                    resource: action.Resource, operation: action.Operation, decision: "allow", reasonCode: reason,
                    executionState: state.ToString(), attempt: action.Attempt, idempotencyKey: action.IdempotencyKey,
                    evidenceRef: $"action:{action.Id}"));
                return true;
            }, CancellationToken.None);
        }
        catch (StoreUnavailableException)
        {
            // The primary record of the result could not be written. Do not hand the
            // data over as if nothing happened; the action is left for the stale sweep.
            return new ExecutionOutcome(ExecutionStatus.Failed, ReasonCodes.ResultNotRecorded, action.Id, null);
        }

        var status = state switch
        {
            ActionState.Succeeded => ExecutionStatus.Succeeded,
            ActionState.Unknown => ExecutionStatus.Unknown,
            _ => ExecutionStatus.Failed,
        };
        return new ExecutionOutcome(status, reason, action.Id, payload);
    }

    /// <summary>
    /// Closes actions that were admitted but never got a recorded result (process
    /// crash, store outage). Their real outcome is not known, so they become Unknown.
    /// </summary>
    public Task<int> SweepStaleActionsAsync(Guid tenantId, Guid agentId, TimeSpan olderThan, CancellationToken cancellationToken) =>
        store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var stale = (await tx.ListActionsAsync(tenantId, agentId))
                .Where(a => a.State is ActionState.Admitted or ActionState.Executing && a.AdmittedAt <= now - olderThan)
                .ToList();
            foreach (var action in stale)
            {
                var state = action.State == ActionState.Admitted ? ActionState.Cancelled : ActionState.Unknown;
                await tx.UpdateActionAsync(action with { State = state, CompletedAt = now, ReasonCode = ReasonCodes.StaleExecution });
                await tx.AppendAuditAsync(Audit.Create(now, tenantId, agentId, EventTypes.ActionCompleted, action.CorrelationId, AccessModes.System,
                    policyVersion: action.PolicyVersion, revocationEpoch: action.RevocationEpoch, capabilityId: action.CapabilityId,
                    resource: action.Resource, operation: action.Operation, reasonCode: ReasonCodes.StaleExecution,
                    executionState: state.ToString(), attempt: action.Attempt, idempotencyKey: action.IdempotencyKey,
                    evidenceRef: $"action:{action.Id}"));
            }

            return stale.Count;
        }, cancellationToken);

    private async Task<Admission> AdmitAsync(IWalletTx tx, CallerIdentity caller, ExecutionRequest request, ResolvedOperation? operation, string invalidReason, string correlationId)
    {
        var now = time.GetUtcNow();
        var found = await tx.FindAgentByIdentityAsync(caller.TenantId, caller.ObjectId);
        // Re-read through the locking path so admission and suspension are serialised.
        var agent = found is null ? null : await tx.GetAgentAsync(caller.TenantId, found.Id);

        async Task<Admission> DenyAsync(string reason)
        {
            await tx.AppendAuditAsync(Audit.Create(now, caller.TenantId, agent?.Id, EventTypes.ActionDenied, correlationId, AccessModes.Application,
                callerIdentity: caller.ObjectId, policyVersion: agent?.PolicyVersion, revocationEpoch: agent?.RevocationEpoch,
                resource: operation?.CanonicalResource, operation: operation?.Operation, decision: "deny", reasonCode: reason,
                idempotencyKey: operation is null ? null : request.IdempotencyKey));
            if (reason == ReasonCodes.AgentSuspended)
            {
                await tx.AppendAuditAsync(Audit.Create(now, caller.TenantId, agent!.Id, EventTypes.AlertPostSuspensionAttempt, correlationId,
                    AccessModes.System, callerIdentity: caller.ObjectId, revocationEpoch: agent.RevocationEpoch, reasonCode: reason));
            }

            return new Admission(new ExecutionOutcome(ExecutionStatus.Denied, reason, null, null), null, null);
        }

        if (agent is null)
        {
            return await DenyAsync(ReasonCodes.AgentUnknown);
        }

        if (operation is null)
        {
            return await DenyAsync(invalidReason);
        }

        var existing = await tx.FindActionByIdempotencyKeyAsync(agent.TenantId, agent.Id, request.IdempotencyKey);
        if (existing is not null)
        {
            if (existing.Operation != operation.Operation || existing.ArgumentsDigest != operation.ArgumentsDigest)
            {
                return await DenyAsync(ReasonCodes.IdempotencyConflict);
            }

            // Same key, same arguments: report the earlier outcome, never run the effect twice.
            // Content is not stored, so a replay carries no payload.
            var replayStatus = existing.State switch
            {
                ActionState.Succeeded => ExecutionStatus.Succeeded,
                ActionState.Failed => ExecutionStatus.Failed,
                ActionState.Cancelled => ExecutionStatus.Cancelled,
                _ => ExecutionStatus.Unknown,
            };
            var replayReason = existing.State is ActionState.Admitted or ActionState.Executing ? ReasonCodes.InProgress : ReasonCodes.IdempotentReplay;
            return new Admission(new ExecutionOutcome(replayStatus, replayReason, existing.Id, null, Replayed: true), null, null);
        }

        var capabilities = await tx.ListCapabilitiesAsync(agent.TenantId, agent.Id);
        var recent = await tx.CountActionsSinceAsync(agent.TenantId, agent.Id, now - TimeSpan.FromHours(1));
        var decision = AuthorizationEngine.Evaluate(agent, capabilities, operation, recent, now);
        if (!decision.Allowed)
        {
            return await DenyAsync(decision.ReasonCode);
        }

        var action = new ActionRecord(Guid.NewGuid(), agent.TenantId, agent.Id, operation.Operation, operation.CanonicalResource,
            operation.ArgumentsDigest, decision.Capability!.Id, agent.PolicyVersion, agent.RevocationEpoch, ActionState.Admitted,
            request.IdempotencyKey, correlationId, 1, now, null, null);
        await tx.InsertActionAsync(action);
        await tx.AppendAuditAsync(Audit.Create(now, agent.TenantId, agent.Id, EventTypes.ActionAdmitted, correlationId, AccessModes.Application,
            callerIdentity: caller.ObjectId, executorIdentity: agent.ExecutorObjectId, policyVersion: agent.PolicyVersion,
            revocationEpoch: agent.RevocationEpoch, capabilityId: action.CapabilityId, resource: action.Resource,
            operation: action.Operation, decision: "allow", reasonCode: ReasonCodes.Ok, executionState: ActionState.Admitted.ToString(),
            attempt: 1, idempotencyKey: action.IdempotencyKey, evidenceRef: $"action:{action.Id}"));
        return new Admission(null, action, agent);
    }

    private async Task<string?> RevalidateAsync(IWalletTx tx, CallerIdentity caller, ActionRecord action, string correlationId)
    {
        var now = time.GetUtcNow();
        var agent = await tx.GetAgentAsync(action.TenantId, action.AgentId);
        var capability = await tx.GetCapabilityAsync(action.TenantId, action.CapabilityId);
        var problem = AuthorizationEngine.Revalidate(agent, capability, action, now);

        var state = problem is null ? ActionState.Executing : ActionState.Cancelled;
        await tx.UpdateActionAsync(action with { State = state, CompletedAt = problem is null ? null : now, ReasonCode = problem });
        await tx.AppendAuditAsync(Audit.Create(now, action.TenantId, action.AgentId,
            problem is null ? EventTypes.ActionExecuting : EventTypes.ActionCancelled, correlationId, AccessModes.Application,
            callerIdentity: caller.ObjectId, executorIdentity: agent?.ExecutorObjectId, policyVersion: agent?.PolicyVersion,
            revocationEpoch: agent?.RevocationEpoch, capabilityId: action.CapabilityId, resource: action.Resource,
            operation: action.Operation, decision: problem is null ? "allow" : "deny", reasonCode: problem ?? ReasonCodes.Ok,
            executionState: state.ToString(), attempt: action.Attempt, idempotencyKey: action.IdempotencyKey,
            evidenceRef: $"action:{action.Id}"));
        return problem;
    }

    private async Task<(ActionState State, string Reason, object? Payload)> CallExternalAsync(Agent agent, ResolvedOperation operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ExternalTimeout);
        try
        {
            object payload = operation.Operation == OperationCatalog.DocumentRead
                ? await gateway.ReadAsync(agent.ExecutorObjectId, operation.Library, operation.ItemId!, timeout.Token)
                : await gateway.ListAsync(agent.ExecutorObjectId, operation.Library, timeout.Token);
            return (ActionState.Succeeded, ReasonCodes.Ok, payload);
        }
        catch (OperationCanceledException)
        {
            // The request may or may not have been served. Say so.
            return (ActionState.Unknown, ReasonCodes.ExternalTimeout, null);
        }
        catch (GatewayThrottledException)
        {
            return (ActionState.Failed, ReasonCodes.ExternalThrottled, null);
        }
        catch (GatewayDeniedException)
        {
            return (ActionState.Failed, ReasonCodes.ExternalDenied, null);
        }
        catch (Exception)
        {
            return (ActionState.Unknown, ReasonCodes.ExternalErrorUnknown, null);
        }
    }

    private sealed record Admission(ExecutionOutcome? Outcome, ActionRecord? Action, Agent? Agent);
}
