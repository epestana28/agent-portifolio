using AgentPortifolio.Core.Domain;

namespace AgentPortifolio.Core.Services;

/// <summary>Identity taken from a validated token. Never from the request body.</summary>
public sealed record CallerIdentity(Guid TenantId, Guid ObjectId);

public sealed record Operator(Guid TenantId, Guid ObjectId, IReadOnlySet<string> Roles)
{
    public bool HasAny(params string[] roles) => roles.Any(Roles.Contains);
}

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class NotFoundException(string message) : Exception(message);

public sealed class InvalidTransitionException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class RequestValidationException(string message) : Exception(message);

internal static class Audit
{
    public const int SchemaVersion = 1;

    public static AuditEvent Create(
        DateTimeOffset now,
        Guid tenantId,
        Guid? agentId,
        string eventType,
        string correlationId,
        string accessMode,
        Guid? humanInitiator = null,
        Guid? callerIdentity = null,
        Guid? executorIdentity = null,
        int? policyVersion = null,
        long? revocationEpoch = null,
        Guid? capabilityId = null,
        string? resource = null,
        string? operation = null,
        string? decision = null,
        string? reasonCode = null,
        string? executionState = null,
        int? attempt = null,
        string? idempotencyKey = null,
        string? evidenceRef = null) =>
        new(Guid.NewGuid(), SchemaVersion, tenantId, agentId, now, now, eventType, correlationId, humanInitiator,
            callerIdentity, executorIdentity, accessMode, policyVersion, revocationEpoch, capabilityId, resource,
            operation, decision, reasonCode, executionState, attempt, idempotencyKey, evidenceRef);
}
