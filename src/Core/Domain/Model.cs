namespace AgentPortifolio.Core.Domain;

public enum AgentState { Draft, PendingReview, Active, Suspended, Retired }

/// <summary>
/// Containment is tracked separately from the agent state on purpose:
/// a suspended agent is only locally blocked, never automatically "verified".
/// </summary>
public enum ContainmentState { Requested, LocalBlocked, ExternalPending, VerifiedScoped, Partial, Failed, Unknown }

public enum StepStatus { Requested, Executed, Verified, Failed, Unknown }

public enum IdentityType { EntraAgentId, ManagedIdentity, ServicePrincipal }

public enum CoverageMode { Unverified, DirectAccessPossible, GatewayEnforced }

public enum ActionState { Admitted, Executing, Succeeded, Failed, Unknown, Cancelled }

public sealed record AgentIdentity(IdentityType Type, Guid ObjectId, Guid? BlueprintId);

public sealed record Coverage(CoverageMode Mode, bool DirectAccessVerifiedAbsent, DateTimeOffset? LastVerifiedAt);

public sealed record WalletLimits(int MaxActionsPerHour);

/// <summary>Agent plus its wallet header. Capabilities live in their own records.</summary>
public sealed record Agent(
    Guid Id,
    Guid TenantId,
    AgentIdentity Identity,
    Guid ExecutorObjectId,
    string Runtime,
    string Purpose,
    Guid OwnerObjectId,
    Guid? SponsorObjectId,
    string Contact,
    AgentState State,
    int PolicyVersion,
    long RevocationEpoch,
    WalletLimits Limits,
    DateTimeOffset WalletExpiresAt,
    Coverage Coverage,
    Guid? ReviewRequestedBy,
    DateTimeOffset? SuspendedAt,
    long Version);

/// <summary>
/// An internal capability is an opaque reference, not a bearer token. It is bound
/// to tenant, agent, action, canonical resource, expiry and revocation epoch.
/// </summary>
public sealed record Capability(
    Guid Id,
    Guid TenantId,
    Guid AgentId,
    string Action,
    string Resource,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    long IssuedEpoch,
    Guid ApprovedBy,
    DateTimeOffset? RevokedAt);

/// <summary>Mirror of an authorisation that exists outside this product (Entra or the resource).</summary>
public sealed record ExternalGrant(
    Guid Id,
    Guid TenantId,
    Guid AgentId,
    Guid PrincipalObjectId,
    string Type,
    string ExternalId,
    string Resource,
    string Role,
    string Source,
    DateTimeOffset LastVerifiedAt,
    string LastResult);

public sealed record ActionRecord(
    Guid Id,
    Guid TenantId,
    Guid AgentId,
    string Operation,
    string Resource,
    string ArgumentsDigest,
    Guid CapabilityId,
    int PolicyVersion,
    long RevocationEpoch,
    ActionState State,
    string IdempotencyKey,
    string CorrelationId,
    int Attempt,
    DateTimeOffset AdmittedAt,
    DateTimeOffset? CompletedAt,
    string? ReasonCode);

public sealed record ContainmentStep(
    string Name,
    string Resource,
    StepStatus Status,
    string? Limitation,
    int Attempts,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastCheckedAt,
    string? Evidence);

public sealed record ContainmentOperation(
    Guid Id,
    Guid TenantId,
    Guid AgentId,
    string Scope,
    Guid? InitiatedBy,
    string Reason,
    ContainmentState State,
    long EpochAfter,
    DateTimeOffset RequestedAt,
    DateTimeOffset? LocalBlockedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ContainmentStep> Steps,
    IReadOnlyList<Guid> InFlightActionIds,
    IReadOnlyList<string> Uncertainties);

/// <summary>Append-only evidence record. Never holds tokens, secrets, prompts or document content.</summary>
public sealed record AuditEvent(
    Guid EventId,
    int SchemaVersion,
    Guid TenantId,
    Guid? AgentId,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string EventType,
    string CorrelationId,
    Guid? HumanInitiator,
    Guid? CallerIdentity,
    Guid? ExecutorIdentity,
    string AccessMode,
    int? PolicyVersion,
    long? RevocationEpoch,
    Guid? CapabilityId,
    string? Resource,
    string? Operation,
    string? Decision,
    string? ReasonCode,
    string? ExecutionState,
    int? Attempt,
    string? IdempotencyKey,
    string? EvidenceRef);

public sealed record OutboxMessage(
    Guid Id,
    Guid TenantId,
    string Type,
    Guid SubjectId,
    long Epoch,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    int Deliveries,
    bool DeadLettered);
