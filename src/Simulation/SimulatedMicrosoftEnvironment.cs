using System.Collections.Concurrent;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Simulation;

public enum Fault { Timeout, Throttle, Fail }

public sealed record SimulatedGrant(Guid PrincipalObjectId, string Type, string ExternalId, string Resource, string Role);

/// <summary>
/// SIMULATION. A small in-memory model of the Microsoft side (identities,
/// resource grants, runtimes, documents, issued tokens) with fault injection.
/// It exists to exercise the control logic without a tenant. Nothing observed
/// here is evidence of how Entra, Graph or SharePoint behave.
/// </summary>
public sealed class SimulatedMicrosoftEnvironment
{
    private readonly ConcurrentDictionary<Guid, bool> identityEnabled = new();
    private readonly ConcurrentDictionary<Guid, bool> runtimeRunning = new();
    private readonly ConcurrentDictionary<string, SimulatedGrant> grants = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, DocumentContent>> libraries = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(Fault Fault, TimeSpan? RetryAfter)>> faults = new();
    private readonly ConcurrentDictionary<string, int> calls = new();
    private readonly ConcurrentDictionary<string, (Guid Principal, DateTimeOffset ExpiresAt)> tokens = new();

    /// <summary>
    /// When false (the default, and the pessimistic case), a token issued before the
    /// identity was disabled keeps working until it expires.
    /// </summary>
    public bool RevocationReachesIssuedTokens { get; set; }

    /// <summary>When set, document reads wait on it. Used to hold an action in flight.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>Completed when a read reaches the simulated service.</summary>
    public TaskCompletionSource ReadStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void AddIdentity(Guid objectId) => identityEnabled[objectId] = true;

    public void AddRuntime(Guid objectId) => runtimeRunning[objectId] = true;

    public bool IsIdentityEnabled(Guid objectId) => identityEnabled.TryGetValue(objectId, out var enabled) && enabled;

    public bool IsRuntimeRunning(Guid objectId) => runtimeRunning.TryGetValue(objectId, out var running) && running;

    public void DisableIdentity(Guid objectId) => identityEnabled[objectId] = false;

    public void StopRuntime(Guid objectId) => runtimeRunning[objectId] = false;

    public SimulatedGrant AddGrant(Guid principal, string type, string resource, string role)
    {
        var grant = new SimulatedGrant(principal, type, $"sim-grant-{Guid.NewGuid():N}", resource, role);
        grants[grant.ExternalId] = grant;
        return grant;
    }

    public IReadOnlyList<SimulatedGrant> GrantsOf(Guid principal) => grants.Values.Where(g => g.PrincipalObjectId == principal).ToList();

    public void RemoveGrantsOf(Guid principal)
    {
        foreach (var grant in GrantsOf(principal))
        {
            grants.TryRemove(grant.ExternalId, out _);
        }
    }

    public void AddDocument(SharePointLibrary library, string itemId, string name, string text) =>
        libraries.GetOrAdd(library.Canonical, _ => [])[itemId] = new DocumentContent(name, "text/plain", System.Text.Encoding.UTF8.GetBytes(text));

    public string IssueToken(Guid principal, DateTimeOffset expiresAt)
    {
        var token = $"sim-token-{Guid.NewGuid():N}";
        tokens[token] = (principal, expiresAt);
        return token;
    }

    public bool IsTokenAccepted(string token, DateTimeOffset now) =>
        tokens.TryGetValue(token, out var t) && t.ExpiresAt > now && (IsIdentityEnabled(t.Principal) || !RevocationReachesIssuedTokens);

    public IEnumerable<string> TokensOf(Guid principal) => tokens.Where(t => t.Value.Principal == principal).Select(t => t.Key);

    public void InjectFault(string target, Fault fault, TimeSpan? retryAfter = null, int times = 1)
    {
        var queue = faults.GetOrAdd(target, _ => new());
        for (var i = 0; i < times; i++)
        {
            queue.Enqueue((fault, retryAfter));
        }
    }

    public int CallCount(string target) => calls.TryGetValue(target, out var n) ? n : 0;

    /// <summary>Counts the call and applies the next injected fault for the target, if any.</summary>
    internal async Task<AdapterResult?> EnterAsync(string target, CancellationToken ct)
    {
        calls.AddOrUpdate(target, 1, (_, n) => n + 1);
        if (!faults.TryGetValue(target, out var queue) || !queue.TryDequeue(out var next))
        {
            return null;
        }

        switch (next.Fault)
        {
            case Fault.Timeout:
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            case Fault.Throttle:
                return new AdapterResult(AdapterOutcome.Throttled, "simulated 429", next.RetryAfter);
            default:
                return new AdapterResult(AdapterOutcome.Failed, "simulated failure");
        }
    }

    internal bool TryGetLibrary(string canonical, out Dictionary<string, DocumentContent> documents) =>
        libraries.TryGetValue(canonical, out documents!);

    internal void SignalReadStarted() => ReadStarted.TrySetResult();

    public void ResetReadStarted() => ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
