using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Services;

namespace AgentPortifolio.Tests.Integration;

public class LifecycleTests
{
    [Fact]
    public async Task BT19_Operator_without_the_role_cannot_suspend_reactivate_or_grant()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var grant = new GrantCapabilityCommand(OperationCatalog.DocumentRead, TestWorld.Approved.Canonical, world.Time.GetUtcNow().AddDays(1));

        foreach (var op in new[] { world.ReaderOnly, world.Auditor })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.SuspendAsync(op, agent.Id, "x", "test", default));
            await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.GrantCapabilityAsync(op, agent.Id, grant, "test", default));
            await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.RequestReactivationAsync(op, agent.Id, "test", default));
            await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.ApproveReactivationAsync(op, agent.Id, "test", default));
        }

        // An incident operator can stop an agent fast, but cannot raise privileges or reactivate.
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.GrantCapabilityAsync(world.Incident, agent.Id, grant, "test", default));
        await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.RequestReactivationAsync(world.Incident, agent.Id, "test", default));
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.ReadAuditAsync(world.AdminA, agent.Id, default));
        Assert.Equal(AgentState.Suspended, (await world.AgentAsync(agent.Id)).State);
    }

    [Fact]
    public async Task Activation_needs_a_second_person()
    {
        var world = new TestWorld();
        var draft = await world.Control.RegisterAsync(world.AdminA, new RegisterAgentCommand(IdentityType.ServicePrincipal, Guid.NewGuid(), null,
            Guid.NewGuid(), "rt", "purpose", Guid.NewGuid(), null, "c", 10, world.Time.GetUtcNow().AddDays(1)), "test", default);
        await world.Control.SubmitForReviewAsync(world.AdminA, draft.Id, "test", default);

        await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.ApproveAsync(world.AdminA, draft.Id, "test", default));
        Assert.Equal(AgentState.Active, (await world.Control.ApproveAsync(world.AdminB, draft.Id, "test", default)).State);
    }

    [Fact]
    public async Task Agent_without_owner_or_with_shared_executor_identity_cannot_be_registered()
    {
        var world = new TestWorld();
        var identity = Guid.NewGuid();
        RegisterAgentCommand Command(Guid owner, Guid executor) =>
            new(IdentityType.ServicePrincipal, identity, null, executor, "rt", "purpose", owner, null, "c", 10, world.Time.GetUtcNow().AddDays(1));

        await Assert.ThrowsAsync<RequestValidationException>(() => world.Control.RegisterAsync(world.AdminA, Command(Guid.Empty, Guid.NewGuid()), "test", default));
        await Assert.ThrowsAsync<RequestValidationException>(() => world.Control.RegisterAsync(world.AdminA, Command(Guid.NewGuid(), identity), "test", default));
    }

    [Fact]
    public async Task Reactivation_needs_second_person_revalidated_grants_and_kills_old_capabilities()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Time.Advance(TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<InvalidTransitionException>(() => world.Control.ApproveReactivationAsync(world.AdminB, agent.Id, "test", default));
        await world.Control.RequestReactivationAsync(world.AdminA, agent.Id, "test", default);
        await Assert.ThrowsAsync<ForbiddenException>(() => world.Control.ApproveReactivationAsync(world.AdminA, agent.Id, "test", default));

        // Grants were last verified before the suspension: not good enough.
        var stale = await Assert.ThrowsAsync<InvalidTransitionException>(() => world.Control.ApproveReactivationAsync(world.AdminB, agent.Id, "test", default));
        Assert.Equal("grants_not_revalidated", stale.ReasonCode);

        await world.Reconciliation.ReconcileAsync(agent.TenantId, agent.Id, "test", default);
        var reactivated = await world.Control.ApproveReactivationAsync(world.AdminB, agent.Id, "test", default);

        Assert.Equal(AgentState.Active, reactivated.State);
        Assert.Equal(agent.RevocationEpoch + 2, reactivated.RevocationEpoch);
        Assert.True(reactivated.PolicyVersion > agent.PolicyVersion);
        // Capabilities from before the suspension stay dead until granted again.
        Assert.Equal(ReasonCodes.CapabilityStaleEpoch, (await world.ReadAsync(agent)).ReasonCode);
        await world.GrantAsync(agent.Id, OperationCatalog.DocumentRead, TestWorld.Approved);
        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);
    }

    [Fact]
    public async Task Old_containment_message_never_acts_on_a_reactivated_agent_and_never_reactivates()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        world.Time.Advance(TimeSpan.FromMinutes(10));
        await world.Control.RequestReactivationAsync(world.AdminA, agent.Id, "test", default);
        await world.Reconciliation.ReconcileAsync(agent.TenantId, agent.Id, "test", default);
        await world.Control.ApproveReactivationAsync(world.AdminB, agent.Id, "test", default);

        // The message of the first suspension is only delivered now.
        await world.Containment.ProcessPendingAsync(default);

        var operation = Assert.Single(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));
        Assert.Equal(ContainmentState.Failed, operation.State);
        Assert.Contains(operation.Uncertainties, u => u.StartsWith("Superseded", StringComparison.Ordinal));
        Assert.True(world.Environment.IsIdentityEnabled(agent.Identity.ObjectId));
        Assert.All(world.Adapters, a => Assert.Equal(0, world.Environment.CallCount(a.Name)));
        Assert.Equal(AgentState.Active, (await world.AgentAsync(agent.Id)).State);
    }

    [Fact]
    public async Task Suspend_is_idempotent_and_does_not_bump_the_epoch_twice()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();

        var first = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        var second = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident again", "test", default);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(agent.RevocationEpoch + 1, (await world.AgentAsync(agent.Id)).RevocationEpoch);
    }

    [Fact]
    public async Task Capability_must_be_allowlisted_canonical_and_short_lived()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        var soon = world.Time.GetUtcNow().AddDays(1);

        await Assert.ThrowsAsync<RequestValidationException>(() => world.Control.GrantCapabilityAsync(world.AdminA, agent.Id,
            new GrantCapabilityCommand("mail.send", TestWorld.Approved.Canonical, soon), "test", default));
        await Assert.ThrowsAsync<RequestValidationException>(() => world.Control.GrantCapabilityAsync(world.AdminA, agent.Id,
            new GrantCapabilityCommand(OperationCatalog.DocumentRead, "sharepoint:site/*/drive/*", soon), "test", default));
        await Assert.ThrowsAsync<RequestValidationException>(() => world.Control.GrantCapabilityAsync(world.AdminA, agent.Id,
            new GrantCapabilityCommand(OperationCatalog.DocumentRead, TestWorld.Approved.Canonical, world.Time.GetUtcNow().AddDays(365)), "test", default));
    }

    [Fact]
    public async Task Retired_agent_stays_denied()
    {
        var world = new TestWorld();
        var agent = await world.ActiveAgentAsync();
        await world.Control.RetireAsync(world.AdminA, agent.Id, "test", default);

        Assert.Equal(ReasonCodes.AgentNotActive, (await world.ReadAsync(agent)).ReasonCode);
        await Assert.ThrowsAsync<InvalidTransitionException>(() => world.Control.RequestReactivationAsync(world.AdminA, agent.Id, "test", default));
    }
}
