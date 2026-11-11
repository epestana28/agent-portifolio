using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Simulation;

/// <summary>SIMULATION of the data path. Models a resource-scoped grant: no grant on the library, no access.</summary>
public sealed class SimulatedDocumentGateway(SimulatedMicrosoftEnvironment environment) : IDocumentGateway
{
    public const string Target = "graph.documents";

    public async Task<DocumentContent> ReadAsync(Guid executorObjectId, SharePointLibrary library, string itemId, CancellationToken cancellationToken)
    {
        var documents = await AuthoriseAsync(executorObjectId, library, cancellationToken);
        environment.SignalReadStarted();
        if (environment.ReadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        return documents.TryGetValue(itemId, out var content) ? content : throw new GatewayDeniedException("Item not found.");
    }

    public async Task<IReadOnlyList<DocumentEntry>> ListAsync(Guid executorObjectId, SharePointLibrary library, CancellationToken cancellationToken)
    {
        var documents = await AuthoriseAsync(executorObjectId, library, cancellationToken);
        return documents.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new DocumentEntry(d.Key, d.Value.Name)).ToList();
    }

    private async Task<Dictionary<string, DocumentContent>> AuthoriseAsync(Guid executorObjectId, SharePointLibrary library, CancellationToken ct)
    {
        var fault = await environment.EnterAsync(Target, ct);
        if (fault?.Outcome == AdapterOutcome.Throttled)
        {
            throw new GatewayThrottledException(fault.RetryAfter);
        }

        if (fault is not null)
        {
            throw new InvalidOperationException("Simulated external failure.");
        }

        var granted = environment.IsIdentityEnabled(executorObjectId)
            && environment.GrantsOf(executorObjectId).Any(g => g.Resource == library.Canonical);
        if (!granted || !environment.TryGetLibrary(library.Canonical, out var documents))
        {
            throw new GatewayDeniedException("The executor identity has no grant on this resource.");
        }

        return documents;
    }
}

/// <summary>SIMULATION of reading grants from the external source of truth.</summary>
public sealed class SimulatedGrantSource(SimulatedMicrosoftEnvironment environment) : IExternalGrantSource
{
    public Task<IReadOnlyList<ObservedGrant>> ListAsync(Guid tenantId, Guid principalObjectId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObservedGrant>>(environment.GrantsOf(principalObjectId)
            .Select(g => new ObservedGrant(g.Type, g.ExternalId, g.Resource, g.Role)).ToList());
}

/// <summary>SIMULATION of disabling one agent identity. Siblings of the same blueprint are untouched.</summary>
public sealed class SimulatedIdentityDisableAdapter(SimulatedMicrosoftEnvironment environment) : IContainmentAdapter
{
    public string Name => "identity.disable";

    public string? StandingLimitation => "Disabling blocks new token issuance. It does not prove that tokens issued earlier stopped working.";

    public bool AppliesTo(Agent agent) => agent.Identity.Type != IdentityType.ManagedIdentity;

    public string DescribeResource(Agent agent) => $"identity {agent.Identity.ObjectId} ({agent.Identity.Type})";

    public async Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken)
    {
        if (await environment.EnterAsync(Name, cancellationToken) is { } fault)
        {
            return fault;
        }

        environment.DisableIdentity(agent.Identity.ObjectId);
        return new AdapterResult(AdapterOutcome.Done, "simulated: accountEnabled=false accepted");
    }

    public Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken) =>
        Task.FromResult(environment.IsIdentityEnabled(agent.Identity.ObjectId)
            ? new AdapterResult(AdapterOutcome.Failed, "simulated: identity still enabled")
            : new AdapterResult(AdapterOutcome.Done, "simulated: identity read back as disabled"));
}

/// <summary>SIMULATION of removing the resource grants of the agent and of its dedicated executor identity.</summary>
public sealed class SimulatedGrantRemovalAdapter(SimulatedMicrosoftEnvironment environment) : IContainmentAdapter
{
    public string Name => "grants.remove";

    public string? StandingLimitation => "Covers only the grants known to this product. Other access paths must be inventoried separately.";

    public bool AppliesTo(Agent agent) => true;

    public string DescribeResource(Agent agent) => $"resource grants of executor {agent.ExecutorObjectId} and agent {agent.Identity.ObjectId}";

    public async Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken)
    {
        if (await environment.EnterAsync(Name, cancellationToken) is { } fault)
        {
            return fault;
        }

        environment.RemoveGrantsOf(agent.ExecutorObjectId);
        environment.RemoveGrantsOf(agent.Identity.ObjectId);
        return new AdapterResult(AdapterOutcome.Done, "simulated: grants deleted");
    }

    public Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken)
    {
        var remaining = environment.GrantsOf(agent.ExecutorObjectId).Count + environment.GrantsOf(agent.Identity.ObjectId).Count;
        return Task.FromResult(remaining == 0
            ? new AdapterResult(AdapterOutcome.Done, "simulated: no grants listed")
            : new AdapterResult(AdapterOutcome.Failed, $"simulated: {remaining} grant(s) still listed"));
    }
}

/// <summary>SIMULATION of stopping or isolating the agent runtime.</summary>
public sealed class SimulatedRuntimeIsolationAdapter(SimulatedMicrosoftEnvironment environment) : IContainmentAdapter
{
    public string Name => "runtime.isolate";

    public string? StandingLimitation => null;

    public bool AppliesTo(Agent agent) => true;

    public string DescribeResource(Agent agent) => $"runtime '{agent.Runtime}'";

    public async Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken)
    {
        if (await environment.EnterAsync(Name, cancellationToken) is { } fault)
        {
            return fault;
        }

        environment.StopRuntime(agent.Identity.ObjectId);
        return new AdapterResult(AdapterOutcome.Done, "simulated: runtime stop accepted");
    }

    public Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken) =>
        Task.FromResult(environment.IsRuntimeRunning(agent.Identity.ObjectId)
            ? new AdapterResult(AdapterOutcome.Failed, "simulated: runtime still running")
            : new AdapterResult(AdapterOutcome.Done, "simulated: runtime not running"));
}

/// <summary>
/// SIMULATION of probing with a token issued before the suspension. It is a
/// measurement, not an action: the step is only verified when no earlier token
/// is still accepted.
/// </summary>
public sealed class SimulatedIssuedTokenProbeAdapter(SimulatedMicrosoftEnvironment environment, TimeProvider time) : IContainmentAdapter
{
    public string Name => "tokens.probe";

    public string? StandingLimitation => "The probe covers one test resource and the tokens known to the probe. It says nothing about other resources.";

    public bool AppliesTo(Agent agent) => true;

    public string DescribeResource(Agent agent) => $"tokens issued earlier to {agent.Identity.ObjectId}";

    public async Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken) =>
        await environment.EnterAsync(Name, cancellationToken) ?? new AdapterResult(AdapterOutcome.Done, "simulated: probe sent");

    public Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken)
    {
        var accepted = environment.TokensOf(agent.Identity.ObjectId).Count(t => environment.IsTokenAccepted(t, time.GetUtcNow()));
        return Task.FromResult(accepted == 0
            ? new AdapterResult(AdapterOutcome.Done, "simulated: no earlier token accepted")
            : new AdapterResult(AdapterOutcome.Failed, $"simulated: {accepted} earlier token(s) still accepted"));
    }
}
