using AgentPortifolio.Core.Domain;

namespace AgentPortifolio.Core.Ports;

/// <summary>
/// The authoritative state. Every decision and every state change runs inside
/// <see cref="TransactAsync{T}"/>: either all writes of the callback are
/// persisted, audit and outbox included, or none is.
/// </summary>
public interface IWalletStore
{
    Task<T> TransactAsync<T>(Func<IWalletTx, Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>Every query takes the tenant. An identifier alone is never an authorisation.</summary>
public interface IWalletTx
{
    /// <summary>Reads the agent and holds it locked until the transaction ends.</summary>
    Task<Agent?> GetAgentAsync(Guid tenantId, Guid agentId);
    Task<Agent?> FindAgentByIdentityAsync(Guid tenantId, Guid identityObjectId);
    Task<IReadOnlyList<Agent>> ListAgentsAsync(Guid tenantId);
    Task InsertAgentAsync(Agent agent);
    /// <summary>Optimistic concurrency: fails when <see cref="Agent.Version"/> is stale.</summary>
    Task<Agent> UpdateAgentAsync(Agent agent);

    Task<Capability?> GetCapabilityAsync(Guid tenantId, Guid capabilityId);
    Task<IReadOnlyList<Capability>> ListCapabilitiesAsync(Guid tenantId, Guid agentId);
    Task InsertCapabilityAsync(Capability capability);
    Task UpdateCapabilityAsync(Capability capability);

    Task<IReadOnlyList<ExternalGrant>> ListGrantsAsync(Guid tenantId, Guid agentId);
    Task UpsertGrantAsync(ExternalGrant grant);

    Task<ActionRecord?> GetActionAsync(Guid tenantId, Guid actionId);
    Task<ActionRecord?> FindActionByIdempotencyKeyAsync(Guid tenantId, Guid agentId, string idempotencyKey);
    Task<int> CountActionsSinceAsync(Guid tenantId, Guid agentId, DateTimeOffset since);
    Task<IReadOnlyList<ActionRecord>> ListActionsAsync(Guid tenantId, Guid agentId);
    Task InsertActionAsync(ActionRecord action);
    Task UpdateActionAsync(ActionRecord action);

    Task<ContainmentOperation?> GetContainmentAsync(Guid tenantId, Guid operationId);
    Task<IReadOnlyList<ContainmentOperation>> ListContainmentsAsync(Guid tenantId, Guid agentId);
    Task InsertContainmentAsync(ContainmentOperation operation);
    Task UpdateContainmentAsync(ContainmentOperation operation);

    Task AppendAuditAsync(AuditEvent auditEvent);
    Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(Guid tenantId, Guid? agentId);

    Task EnqueueOutboxAsync(OutboxMessage message);
    /// <summary>Worker only. The single query that crosses tenants.</summary>
    Task<IReadOnlyList<OutboxMessage>> ListPendingOutboxAsync(int max);
    Task UpdateOutboxAsync(OutboxMessage message);
}

public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class ConcurrencyConflictException(string message) : Exception(message);

public sealed record DocumentContent(string Name, string ContentType, byte[] Bytes);

public sealed record DocumentEntry(string ItemId, string Name);

/// <summary>
/// The only way to the data. The implementation holds the isolated credential;
/// the agent never receives it.
/// </summary>
public interface IDocumentGateway
{
    Task<DocumentContent> ReadAsync(Guid executorObjectId, SharePointLibrary library, string itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentEntry>> ListAsync(Guid executorObjectId, SharePointLibrary library, CancellationToken cancellationToken);
}

public sealed class GatewayThrottledException(TimeSpan? retryAfter) : Exception("External service throttled the request.")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class GatewayDeniedException(string message) : Exception(message);

public sealed record ObservedGrant(string Type, string ExternalId, string Resource, string Role);

/// <summary>Reads what the external source of truth actually grants to a principal.</summary>
public interface IExternalGrantSource
{
    Task<IReadOnlyList<ObservedGrant>> ListAsync(Guid tenantId, Guid principalObjectId, CancellationToken cancellationToken);
}

public enum AdapterOutcome { Done, Throttled, Failed }

public sealed record AdapterResult(AdapterOutcome Outcome, string? Evidence = null, TimeSpan? RetryAfter = null);

/// <summary>
/// One external containment action. <see cref="ExecuteAsync"/> must be
/// idempotent. <see cref="VerifyAsync"/> is an independent probe: an
/// administrative "done" is not the same as an observed effect.
/// </summary>
public interface IContainmentAdapter
{
    string Name { get; }
    bool AppliesTo(Agent agent);
    string DescribeResource(Agent agent);
    /// <summary>Known limit of this action, always shown in the report.</summary>
    string? StandingLimitation { get; }
    Task<AdapterResult> ExecuteAsync(Agent agent, CancellationToken cancellationToken);
    Task<AdapterResult> VerifyAsync(Agent agent, CancellationToken cancellationToken);
}
