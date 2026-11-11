using System.Text.Json;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Simulation;

namespace AgentPortifolio.Tests.Integration;

/// <summary>
/// Blocking tests of the specification (section 15), run against the local slice.
/// The Microsoft side is simulated: these prove the control logic only.
/// </summary>
public class VerticalSliceTests
{
    [Fact]
    public async Task BT01_Authorised_resource_works_and_unauthorised_resource_fails()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();

        var allowed = await world.ReadAsync(agent);
        Assert.Equal(ExecutionStatus.Succeeded, allowed.Status);
        Assert.Equal(TestWorld.SecretText, System.Text.Encoding.UTF8.GetString(Assert.IsType<DocumentContent>(allowed.Payload).Bytes));

        var denied = await world.ReadAsync(agent, TestWorld.Other);
        Assert.Equal(ExecutionStatus.Denied, denied.Status);
        Assert.Equal(ReasonCodes.CapabilityMissing, denied.ReasonCode);
    }

    [Fact]
    public async Task BT01_Effective_access_is_the_intersection_of_wallet_and_external_grant()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        // The wallet now allows the other library, but the executor has no external grant there.
        await world.GrantAsync(agent.Id, OperationCatalog.DocumentRead, TestWorld.Other);

        var outcome = await world.ReadAsync(agent, TestWorld.Other);

        Assert.Equal(ExecutionStatus.Failed, outcome.Status);
        Assert.Equal(ReasonCodes.ExternalDenied, outcome.ReasonCode);
        Assert.Null(outcome.Payload);
    }

    [Fact]
    public async Task BT03_Suspension_invalidates_a_capability_that_is_still_inside_its_validity()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);

        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        var after = await world.ReadAsync(agent);

        Assert.Equal(ContainmentState.LocalBlocked, operation.State);
        Assert.Equal(agent.RevocationEpoch + 1, operation.EpochAfter);
        Assert.Equal(ExecutionStatus.Denied, after.Status);
        Assert.Equal(ReasonCodes.AgentSuspended, after.ReasonCode);
        Assert.Contains(await world.AuditAsync(agent.Id), e => e.EventType == EventTypes.AlertPostSuspensionAttempt);
    }

    [Fact]
    public async Task BT04_Race_between_suspension_and_execution_reports_exactly_the_actions_in_flight()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        world.Environment.ReadGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The action passes admission and revalidation, then waits inside the external call.
        var inFlight = world.ReadAsync(agent);
        await world.Environment.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        var afterSuspension = await world.ReadAsync(agent);
        world.Environment.ReadGate.SetResult();
        var completed = await inFlight;

        // The in-flight action is named, not hidden, and its real outcome is recorded.
        Assert.Equal([completed.ActionId!.Value], operation.InFlightActionIds);
        Assert.NotEmpty(operation.Uncertainties);
        Assert.Equal(ExecutionStatus.Succeeded, completed.Status);
        Assert.Equal(ExecutionStatus.Denied, afterSuspension.Status);
        var actions = await world.ActionsAsync(agent);
        Assert.Equal(ActionState.Succeeded, Assert.Single(actions).State);
    }

    [Fact]
    public async Task BT04_Suspension_between_admission_and_revalidation_cancels_before_any_external_effect()
    {
        // A gateway that must never be reached.
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var raced = new RacingStore(world.Memory);
        var execution = new ExecutionService(raced, world.Gateway, world.Time, new ExecutionOptions());
        raced.BeforeSecondTransaction = () => world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);

        var outcome = await execution.ExecuteAsync(world.Caller(agent), TestWorld.Read(TestWorld.Approved), "test", default);

        Assert.Equal(ExecutionStatus.Cancelled, outcome.Status);
        Assert.Equal(ReasonCodes.AgentSuspended, outcome.ReasonCode);
        Assert.Equal(0, world.Environment.CallCount(SimulatedDocumentGateway.Target));
        Assert.Equal(ActionState.Cancelled, Assert.Single(await world.ActionsAsync(agent)).State);
    }

    [Fact]
    public async Task BT07_Revoking_one_capability_does_not_change_the_others()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var view = await world.Control.GetAgentAsync(world.AdminA, agent.Id, default);
        var read = view.Capabilities.Single(c => c.Action == OperationCatalog.DocumentRead);

        await world.Control.RevokeCapabilityAsync(world.Incident, agent.Id, read.Id, "test", default);

        var readOutcome = await world.ReadAsync(agent);
        var listOutcome = await world.Execution.ExecuteAsync(world.Caller(agent), TestWorld.List(TestWorld.Approved), "test", default);
        var after = await world.AgentAsync(agent.Id);
        Assert.Equal(ReasonCodes.CapabilityRevoked, readOutcome.ReasonCode);
        Assert.Equal(ExecutionStatus.Succeeded, listOutcome.Status);
        Assert.Equal(AgentState.Active, after.State);
        Assert.Equal(agent.RevocationEpoch, after.RevocationEpoch);
        Assert.Equal(agent.PolicyVersion + 1, after.PolicyVersion);
    }

    [Fact]
    public async Task BT10_Repeated_request_with_the_same_key_does_not_repeat_the_effect()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var key = TestWorld.NewKey();

        var first = await world.ReadAsync(agent, key: key);
        var second = await world.ReadAsync(agent, key: key);
        var conflicting = await world.Execution.ExecuteAsync(world.Caller(agent), TestWorld.List(TestWorld.Approved, key), "test", default);

        Assert.Equal(ExecutionStatus.Succeeded, first.Status);
        Assert.True(second.Replayed);
        Assert.Equal(first.ActionId, second.ActionId);
        Assert.Null(second.Payload);
        Assert.Equal(1, world.Environment.CallCount(SimulatedDocumentGateway.Target));
        Assert.Equal(ReasonCodes.IdempotencyConflict, conflicting.ReasonCode);
        Assert.Single(await world.ActionsAsync(agent));
    }

    [Fact]
    public async Task BT10_Redelivered_containment_message_does_not_repeat_verified_steps()
    {
        var world = new TestWorld();
        world.Environment.RevocationReachesIssuedTokens = true;
        var agent = await world.ActiveAgentAsync();
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);

        await world.Containment.RunAsync(agent.TenantId, operation.Id, default);
        var again = await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        Assert.Equal(ContainmentState.VerifiedScoped, again.State);
        Assert.All(world.Adapters, a => Assert.Equal(1, world.Environment.CallCount(a.Name)));
    }

    [Fact]
    public async Task BT13_Disabling_one_agent_does_not_disable_siblings_of_the_same_blueprint()
    {
        var world = new TestWorld();
        var blueprint = Guid.NewGuid();
        var first = await world.ActiveAgentAsync(blueprint);
        var sibling = await world.ActiveAgentAsync(blueprint);

        var operation = await world.Control.SuspendAsync(world.Incident, first.Id, "incident", "test", default);
        await world.Containment.RunAsync(first.TenantId, operation.Id, default);

        Assert.False(world.Environment.IsIdentityEnabled(first.Identity.ObjectId));
        Assert.True(world.Environment.IsIdentityEnabled(sibling.Identity.ObjectId));
        Assert.Equal(AgentState.Active, (await world.AgentAsync(sibling.Id)).State);
        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(sibling)).Status);
        Assert.Equal(ExecutionStatus.Denied, (await world.ReadAsync(first)).Status);
    }

    [Fact]
    public async Task BT15_Broad_external_grant_is_detected_and_suspends_the_agent()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        world.Environment.AddGrant(agent.ExecutorObjectId, "Sites.Read.All", "tenant", "read");

        var report = await world.Reconciliation.ReconcileAsync(agent.TenantId, agent.Id, "test", default);

        Assert.True(report.Suspended);
        Assert.Contains(report.Findings, f => f is { Kind: "broad_grant", Dangerous: true });
        Assert.Equal(AgentState.Suspended, (await world.AgentAsync(agent.Id)).State);
        Assert.Equal(CoverageMode.DirectAccessPossible, report.Coverage);
        Assert.Contains(await world.AuditAsync(agent.Id), e => e.EventType == EventTypes.AlertBroadGrant);
        Assert.Equal(ExecutionStatus.Denied, (await world.ReadAsync(agent)).Status);
    }

    [Fact]
    public async Task BT15_Direct_grant_on_the_agent_identity_lowers_coverage_and_clean_state_restores_it()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();

        var clean = await world.Reconciliation.ReconcileAsync(agent.TenantId, agent.Id, "test", default);
        world.Environment.AddGrant(agent.Identity.ObjectId, "Sites.Selected", TestWorld.Approved.Canonical, "read");
        var drifted = await world.Reconciliation.ReconcileAsync(agent.TenantId, agent.Id, "test", default);

        Assert.Empty(clean.Findings);
        Assert.Equal(CoverageMode.GatewayEnforced, clean.Coverage);
        Assert.Contains(drifted.Findings, f => f.Kind == "direct_access");
        Assert.Equal(CoverageMode.DirectAccessPossible, drifted.Coverage);
        Assert.False((await world.AgentAsync(agent.Id)).Coverage.DirectAccessVerifiedAbsent);
        Assert.Contains(await world.AuditAsync(agent.Id), e => e.EventType == EventTypes.AlertDirectAccess);
    }

    [Fact]
    public async Task BT18_Ledger_holds_no_document_content_no_item_identifier_and_no_token()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var token = world.Environment.IssueToken(agent.Identity.ObjectId, world.Time.GetUtcNow().AddHours(1));
        await world.ReadAsync(agent);
        await world.ReadAsync(agent, TestWorld.Other);
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        await world.Containment.RunAsync(agent.TenantId, operation.Id, default);

        var evidence = JsonSerializer.Serialize(await world.AuditAsync(agent.Id))
            + JsonSerializer.Serialize(await world.ActionsAsync(agent))
            + JsonSerializer.Serialize(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));

        Assert.DoesNotContain(TestWorld.SecretText, evidence);
        Assert.DoesNotContain(TestWorld.ItemId, evidence);
        Assert.DoesNotContain(token, evidence);
        Assert.DoesNotContain("Bearer", evidence);
    }

    [Fact]
    public async Task Every_decision_leaves_the_minimum_audit_fields()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.ReadAsync(agent);

        var events = await world.AuditAsync(agent.Id);
        var admitted = events.Single(e => e.EventType == EventTypes.ActionAdmitted);
        var completed = events.Single(e => e.EventType == EventTypes.ActionCompleted);

        Assert.Equal(agent.Identity.ObjectId, admitted.CallerIdentity);
        Assert.Equal(agent.ExecutorObjectId, admitted.ExecutorIdentity);
        Assert.Equal(agent.RevocationEpoch, admitted.RevocationEpoch);
        Assert.NotNull(admitted.CapabilityId);
        Assert.NotNull(admitted.IdempotencyKey);
        Assert.Equal(TestWorld.Approved.Canonical, admitted.Resource);
        Assert.Equal("allow", admitted.Decision);
        Assert.Equal(nameof(ActionState.Succeeded), completed.ExecutionState);
        Assert.All(events, e => Assert.Equal(TimeSpan.Zero, e.OccurredAt.Offset));
        // Intent is recorded before the result.
        Assert.True(events.ToList().IndexOf(admitted) < events.ToList().IndexOf(completed));
    }

    [Fact]
    public async Task Tenants_are_isolated_for_operators_and_for_callers()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var foreignAdmin = new Operator(Guid.NewGuid(), Guid.NewGuid(), new HashSet<string> { Roles.PolicyAdmin, Roles.IncidentOperator, Roles.Auditor });

        await Assert.ThrowsAsync<NotFoundException>(() => world.Control.GetAgentAsync(foreignAdmin, agent.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => world.Control.SuspendAsync(foreignAdmin, agent.Id, "x", "test", default));
        Assert.Empty(await world.Control.ReadAuditAsync(foreignAdmin, agent.Id, default));

        // Same object id presented under another tenant resolves to nothing.
        var outcome = await world.Execution.ExecuteAsync(new CallerIdentity(Guid.NewGuid(), agent.Identity.ObjectId), TestWorld.Read(TestWorld.Approved), "test", default);
        Assert.Equal(ReasonCodes.AgentUnknown, outcome.ReasonCode);
    }

    [Fact]
    public async Task Rate_limit_of_the_wallet_is_enforced()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync(maxActionsPerHour: 2);

        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);
        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);
        Assert.Equal(ReasonCodes.RateLimited, (await world.ReadAsync(agent)).ReasonCode);
        world.Time.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);
    }

    /// <summary>Lets a test act between the admission and the revalidation transactions.</summary>
    private sealed class RacingStore(IWalletStore inner) : IWalletStore
    {
        private int calls;

        public Func<Task>? BeforeSecondTransaction { get; set; }

        public async Task<T> TransactAsync<T>(Func<IWalletTx, Task<T>> work, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 2 && BeforeSecondTransaction is { } hook)
            {
                await hook();
            }

            return await inner.TransactAsync(work, cancellationToken);
        }
    }
}
