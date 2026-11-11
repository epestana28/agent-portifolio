using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Services;

namespace AgentPortifolio.Tests.Unit;

public class ResourceAndOperationTests
{
    private const string Site = "contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222";

    [Theory]
    [InlineData("Contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222", "b!d")] // case would need folding
    [InlineData(" contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222", "b!d")] // would need trimming
    [InlineData("contoso.sharepoint.com,11111111-1111-4111-8111-111111111111", "b!d")] // incomplete
    [InlineData("https://contoso.sharepoint.com/sites/x", "b!d")] // a URL is never a resource
    [InlineData(Site, "b!d/../other")] // path traversal
    [InlineData(Site, "b!d%2F")] // encoded
    [InlineData(Site, "b!d\n")] // trailing newline
    [InlineData(Site, "")]
    public void Non_canonical_resources_are_rejected_not_normalised(string site, string drive) =>
        Assert.False(ResourceCanonicalizer.TryLibrary(site, drive, out _));

    [Fact]
    public void Canonical_resource_round_trips()
    {
        Assert.True(ResourceCanonicalizer.TryLibrary(Site, "b!Drive_1-x", out var library));
        Assert.True(ResourceCanonicalizer.TryParseCanonical(library.Canonical, out var parsed));
        Assert.Equal(library, parsed);
    }

    [Theory]
    [InlineData("graph.batch")]
    [InlineData("http.get")]
    [InlineData("sharepoint.document.write")]
    [InlineData("SHAREPOINT.DOCUMENT.READ")]
    public void Operations_outside_the_allowlist_are_rejected(string operation)
    {
        var request = TestWorld.Read(TestWorld.Approved) with { Operation = operation };
        Assert.False(OperationCatalog.TryResolve(request, out _, out var reason));
        Assert.Equal(ReasonCodes.OperationNotAllowlisted, reason);
    }

    [Fact]
    public void Unknown_missing_or_extra_arguments_are_rejected()
    {
        var good = TestWorld.Read(TestWorld.Approved);
        var extra = good with { Arguments = new Dictionary<string, string>(good.Arguments) { ["url"] = "https://evil.example" } };
        var missing = good with { Arguments = good.Arguments.Where(a => a.Key != "itemId").ToDictionary(a => a.Key, a => a.Value) };
        var swapped = good with { Arguments = new Dictionary<string, string> { ["siteId"] = TestWorld.Approved.SiteId, ["driveId"] = TestWorld.Approved.DriveId, ["path"] = "/x" } };

        foreach (var request in new[] { extra, missing, swapped })
        {
            Assert.False(OperationCatalog.TryResolve(request, out _, out var reason));
            Assert.Equal(ReasonCodes.ArgumentsInvalid, reason);
        }
    }

    [Theory]
    [InlineData("../01ITEM")]
    [InlineData("01ITEM/content")]
    [InlineData("01ITEM?select=x")]
    [InlineData("root:/secret.txt:")]
    public void Item_identifiers_cannot_carry_paths_or_queries(string itemId)
    {
        Assert.False(OperationCatalog.TryResolve(TestWorld.Read(TestWorld.Approved, itemId: itemId), out _, out var reason));
        Assert.Equal(ReasonCodes.ResourceNotCanonical, reason);
    }

    [Fact]
    public void Arguments_digest_is_stable_and_does_not_contain_the_arguments()
    {
        Assert.True(OperationCatalog.TryResolve(TestWorld.Read(TestWorld.Approved), out var a, out _));
        Assert.True(OperationCatalog.TryResolve(TestWorld.Read(TestWorld.Approved), out var b, out _));
        Assert.Equal(a.ArgumentsDigest, b.ArgumentsDigest);
        Assert.DoesNotContain(TestWorld.ItemId, a.ArgumentsDigest);
        Assert.Equal(64, a.ArgumentsDigest.Length);
    }
}

public class AuthorizationEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    private static Agent NewAgent(AgentState state = AgentState.Active, long epoch = 3) =>
        new(Guid.NewGuid(), Tenant, new AgentIdentity(IdentityType.EntraAgentId, Guid.NewGuid(), null), Guid.NewGuid(), "rt", "purpose",
            Guid.NewGuid(), null, "c", state, 1, epoch, new WalletLimits(10), Now.AddDays(1), new Coverage(CoverageMode.Unverified, false, null), null, null, 0);

    private static Capability Cap(Agent agent, long? epoch = null, DateTimeOffset? expires = null, DateTimeOffset? revoked = null, string? resource = null) =>
        new(Guid.NewGuid(), agent.TenantId, agent.Id, OperationCatalog.DocumentRead, resource ?? TestWorld.Approved.Canonical, Now.AddDays(-1),
            expires ?? Now.AddDays(1), epoch ?? agent.RevocationEpoch, Guid.NewGuid(), revoked);

    private static ResolvedOperation Operation()
    {
        Assert.True(OperationCatalog.TryResolve(TestWorld.Read(TestWorld.Approved), out var op, out _));
        return op;
    }

    [Fact]
    public void Valid_capability_is_allowed()
    {
        var agent = NewAgent();
        var result = AuthorizationEngine.Evaluate(agent, [Cap(agent)], Operation(), 0, Now);
        Assert.True(result.Allowed);
    }

    [Fact]
    public void Capability_from_an_earlier_epoch_is_dead_even_inside_its_validity()
    {
        var agent = NewAgent(epoch: 4);
        var result = AuthorizationEngine.Evaluate(agent, [Cap(agent, epoch: 3)], Operation(), 0, Now);
        Assert.Equal(ReasonCodes.CapabilityStaleEpoch, result.ReasonCode);
    }

    [Fact]
    public void Expired_revoked_and_missing_capabilities_are_denied_with_distinct_reasons()
    {
        var agent = NewAgent();
        Assert.Equal(ReasonCodes.CapabilityExpired, AuthorizationEngine.Evaluate(agent, [Cap(agent, expires: Now)], Operation(), 0, Now).ReasonCode);
        Assert.Equal(ReasonCodes.CapabilityRevoked, AuthorizationEngine.Evaluate(agent, [Cap(agent, revoked: Now)], Operation(), 0, Now).ReasonCode);
        Assert.Equal(ReasonCodes.CapabilityMissing, AuthorizationEngine.Evaluate(agent, [Cap(agent, resource: TestWorld.Other.Canonical)], Operation(), 0, Now).ReasonCode);
    }

    [Fact]
    public void Capability_of_another_agent_or_tenant_never_matches()
    {
        var agent = NewAgent();
        var foreign = Cap(NewAgent()) with { TenantId = Guid.NewGuid() };
        Assert.Equal(ReasonCodes.CapabilityMissing, AuthorizationEngine.Evaluate(agent, [foreign], Operation(), 0, Now).ReasonCode);
    }

    [Theory]
    [InlineData(AgentState.Draft, ReasonCodes.AgentNotActive)]
    [InlineData(AgentState.PendingReview, ReasonCodes.AgentNotActive)]
    [InlineData(AgentState.Suspended, ReasonCodes.AgentSuspended)]
    [InlineData(AgentState.Retired, ReasonCodes.AgentNotActive)]
    public void Only_active_agents_are_admitted(AgentState state, string reason)
    {
        var agent = NewAgent(state);
        Assert.Equal(reason, AuthorizationEngine.Evaluate(agent, [Cap(agent)], Operation(), 0, Now).ReasonCode);
    }

    [Fact]
    public void Limits_and_wallet_expiry_are_enforced()
    {
        var agent = NewAgent();
        Assert.Equal(ReasonCodes.RateLimited, AuthorizationEngine.Evaluate(agent, [Cap(agent)], Operation(), 10, Now).ReasonCode);
        var expired = agent with { WalletExpiresAt = Now };
        Assert.Equal(ReasonCodes.WalletExpired, AuthorizationEngine.Evaluate(expired, [Cap(expired)], Operation(), 0, Now).ReasonCode);
    }
}

public class ContainmentSummaryTests
{
    private static ContainmentStep Step(StepStatus status) => new("s", "r", status, null, 1, null, null, null);

    [Fact]
    public void Verified_requires_every_step_verified()
    {
        Assert.Equal(ContainmentState.VerifiedScoped, ContainmentOrchestrator.Summarise([Step(StepStatus.Verified), Step(StepStatus.Verified)]));
        Assert.Equal(ContainmentState.Partial, ContainmentOrchestrator.Summarise([Step(StepStatus.Verified), Step(StepStatus.Executed)]));
        Assert.Equal(ContainmentState.Partial, ContainmentOrchestrator.Summarise([Step(StepStatus.Verified), Step(StepStatus.Failed)]));
        Assert.Equal(ContainmentState.Unknown, ContainmentOrchestrator.Summarise([Step(StepStatus.Verified), Step(StepStatus.Unknown)]));
        Assert.Equal(ContainmentState.Failed, ContainmentOrchestrator.Summarise([Step(StepStatus.Failed), Step(StepStatus.Failed)]));
        Assert.Equal(ContainmentState.LocalBlocked, ContainmentOrchestrator.Summarise([]));
    }
}
