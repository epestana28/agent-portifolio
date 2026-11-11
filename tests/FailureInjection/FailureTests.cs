using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Simulation;

namespace AgentPortifolio.Tests.FailureInjection;

public class FailureTests
{
    [Fact]
    public async Task BT08_Store_unavailable_denies_new_protected_actions()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        world.Memory.Available = false;

        var outcome = await world.ReadAsync(agent);

        Assert.Equal(ExecutionStatus.Denied, outcome.Status);
        Assert.Equal(ReasonCodes.StateUnavailable, outcome.ReasonCode);
        Assert.Equal(0, world.Environment.CallCount(SimulatedDocumentGateway.Target));
    }

    [Fact]
    public async Task BT08_Result_that_cannot_be_recorded_is_not_handed_over_as_success()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var failing = new StoreKillingGateway(world.Gateway, world.Memory);
        var execution = new ExecutionService(world.Store, failing, world.Time, new ExecutionOptions());

        var outcome = await execution.ExecuteAsync(world.Caller(agent), TestWorld.Read(TestWorld.Approved), "test", default);
        world.Memory.Available = true;

        Assert.Equal(ExecutionStatus.Failed, outcome.Status);
        Assert.Equal(ReasonCodes.ResultNotRecorded, outcome.ReasonCode);
        Assert.Null(outcome.Payload);
        // The action is left open, and the sweep turns it into Unknown, never into success.
        Assert.Equal(ActionState.Executing, Assert.Single(await world.ActionsAsync(agent)).State);
        world.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await world.Execution.SweepStaleActionsAsync(agent.TenantId, agent.Id, TimeSpan.FromMinutes(1), default));
        Assert.Equal(ActionState.Unknown, Assert.Single(await world.ActionsAsync(agent)).State);
    }

    [Fact]
    public async Task BT09_Bus_unavailable_the_persisted_block_stays_effective()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();

        // No worker runs and no message is ever delivered.
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);

        Assert.Equal(ExecutionStatus.Denied, (await world.ReadAsync(agent)).Status);
        var stored = Assert.Single(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));
        // Local block only: the report must not claim more than that.
        Assert.Equal(ContainmentState.LocalBlocked, stored.State);
        Assert.Equal("local admission only", stored.Scope);
        Assert.Equal(operation.Id, stored.Id);
        Assert.True(world.Environment.IsIdentityEnabled(agent.Identity.ObjectId));
    }

    [Fact]
    public async Task BT11_External_timeout_produces_unknown_then_reconciliation_never_invented_success()
    {
        var world = new TestWorld();
        world.Environment.RevocationReachesIssuedTokens = true;
        var agent = await world.ActiveAgentAsync();
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Environment.InjectFault("identity.disable", Fault.Timeout);

        var result = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.Equal(ContainmentState.Unknown, result.State);
        var step = result.Steps.Single(s => s.Name == "identity.disable");
        Assert.Equal(StepStatus.Unknown, step.Status);
        Assert.Contains("unknown", step.Limitation);
        // The timed-out call is not blindly repeated.
        Assert.Equal(1, world.Environment.CallCount("identity.disable"));

        // Probing shows the identity is still enabled: the step stays unknown.
        var probed = await world.Containment.ReconcileAsync(agent.TenantId, operation.Id, default);
        Assert.Equal(ContainmentState.Unknown, probed.State);

        // A new delivery executes it and the probe confirms it.
        var retried = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);
        Assert.Equal(ContainmentState.VerifiedScoped, retried.State);
    }

    [Fact]
    public async Task BT11_Timeout_of_the_data_call_is_recorded_as_unknown()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        world.Environment.InjectFault(SimulatedDocumentGateway.Target, Fault.Timeout);

        var outcome = await world.ReadAsync(agent);

        Assert.Equal(ExecutionStatus.Unknown, outcome.Status);
        Assert.Equal(ReasonCodes.ExternalTimeout, outcome.ReasonCode);
        Assert.Equal(ActionState.Unknown, Assert.Single(await world.ActionsAsync(agent)).State);
        Assert.Contains(await world.AuditAsync(agent.Id), e => e.ExecutionState == nameof(ActionState.Unknown));
    }

    [Fact]
    public async Task BT12_Throttling_beyond_the_budget_leaves_a_visible_partial_state()
    {
        var world = new TestWorld();
        world.Environment.RevocationReachesIssuedTokens = true;
        var agent = await world.ActiveAgentAsync();
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Environment.InjectFault("grants.remove", Fault.Throttle, TimeSpan.FromMinutes(10));

        var result = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.Equal(ContainmentState.Partial, result.State);
        var step = result.Steps.Single(s => s.Name == "grants.remove");
        Assert.Equal(StepStatus.Requested, step.Status);
        Assert.Contains("Throttled", step.Limitation);
        Assert.DoesNotContain("grants.remove", result.Scope);
        Assert.Contains("identity.disable", result.Scope);
        Assert.NotEmpty(world.Environment.GrantsOf(agent.ExecutorObjectId));
    }

    [Fact]
    public async Task BT12_Throttling_inside_the_budget_honours_retry_after_and_then_completes()
    {
        var world = new TestWorld();
        world.Environment.RevocationReachesIssuedTokens = true;
        var agent = await world.ActiveAgentAsync();
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Environment.InjectFault("grants.remove", Fault.Throttle, TimeSpan.FromSeconds(3), times: 2);

        var result = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.Equal(ContainmentState.VerifiedScoped, result.State);
        Assert.Equal([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)], world.Delays);
        Assert.Equal(3, result.Steps.Single(s => s.Name == "grants.remove").Attempts);
    }

    [Fact]
    public async Task Repeated_failure_is_bounded_and_reported_as_failed()
    {
        var world = new TestWorld();
        world.Environment.RevocationReachesIssuedTokens = true;
        var agent = await world.ActiveAgentAsync();
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Environment.InjectFault("runtime.isolate", Fault.Fail, times: 10);

        var result = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.Equal(ContainmentState.Partial, result.State);
        Assert.Equal(StepStatus.Failed, result.Steps.Single(s => s.Name == "runtime.isolate").Status);
        Assert.Equal(world.ContainmentOptions.MaxAttempts, world.Environment.CallCount("runtime.isolate"));
        // Exponential backoff between attempts.
        Assert.Equal([world.ContainmentOptions.BaseBackoff, world.ContainmentOptions.BaseBackoff * 2], world.Delays);
    }

    [Fact]
    public async Task BT14_SIMULATED_Token_issued_before_suspension_keeps_the_report_from_claiming_verified()
    {
        // SIMULATION of blocking test 14. The real test needs a tenant (phase D).
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var token = world.Environment.IssueToken(agent.Identity.ObjectId, world.Time.GetUtcNow().AddMinutes(90));
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);

        var result = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.True(world.Environment.IsTokenAccepted(token, world.Time.GetUtcNow()));
        Assert.Equal(ContainmentState.Partial, result.State);
        Assert.Equal(StepStatus.Executed, result.Steps.Single(s => s.Name == "tokens.probe").Status);
        Assert.Contains(ContainmentOrchestrator.TokenLimitation, result.Uncertainties);

        // Once the token has expired, a new probe can verify the step.
        world.Time.Advance(TimeSpan.FromMinutes(91));
        var later = await world.Containment.ReconcileAsync(agent.TenantId, operation.Id, default);
        Assert.Equal(ContainmentState.VerifiedScoped, later.State);
        Assert.Contains("verified externally", later.Scope);
        // Even then the standing limitation stays in the report.
        Assert.Contains(ContainmentOrchestrator.TokenLimitation, later.Uncertainties);
    }

    [Fact]
    public async Task Suspended_never_becomes_verified_without_external_steps()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        var noAdapters = new ContainmentOrchestrator(world.Store, [], world.Time, world.ContainmentOptions);

        await noAdapters.ProcessPendingAsync(default);

        var operation = Assert.Single(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));
        Assert.NotEqual(ContainmentState.VerifiedScoped, operation.State);
        Assert.Equal("local admission only", operation.Scope);
    }

    [Fact]
    public async Task Message_that_keeps_failing_is_dead_lettered_and_the_operation_marked_failed()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        var broken = new ContainmentOrchestrator(world.Store, [new ThrowingAdapter()], world.Time, world.ContainmentOptions);

        for (var i = 0; i < world.ContainmentOptions.MaxDeliveries; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => broken.ProcessPendingAsync(default));
        }

        Assert.Equal(1, await broken.ProcessPendingAsync(default));
        Assert.Equal(0, await broken.ProcessPendingAsync(default));
        var operation = Assert.Single(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));
        Assert.Equal(ContainmentState.Failed, operation.State);
        Assert.Contains(operation.Uncertainties, u => u.StartsWith("Dead-lettered", StringComparison.Ordinal));
        Assert.Contains(await world.AuditAsync(agent.Id), e => e.EventType == EventTypes.ContainmentDeadLettered);
        // The local block was never in question.
        Assert.Equal(ExecutionStatus.Denied, (await world.ReadAsync(agent)).Status);
    }

    private sealed class StoreKillingGateway(IDocumentGateway inner, Core.Storage.InMemoryWalletStore store) : IDocumentGateway
    {
        public async Task<DocumentContent> ReadAsync(Guid executorObjectId, SharePointLibrary library, string itemId, CancellationToken cancellationToken)
        {
            var content = await inner.ReadAsync(executorObjectId, library, itemId, cancellationToken);
            store.Available = false;
            return content;
        }

        public Task<IReadOnlyList<DocumentEntry>> ListAsync(Guid executorObjectId, SharePointLibrary library, CancellationToken cancellationToken) =>
            inner.ListAsync(executorObjectId, library, cancellationToken);
    }

    private sealed class ThrowingAdapter : IContainmentAdapter
    {
        public string Name => "broken";
        public string? StandingLimitation => null;
        public bool AppliesTo(Agent agent) => throw new InvalidOperationException("adapter bug");
        public string DescribeResource(Agent agent) => "n/a";
        public Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
