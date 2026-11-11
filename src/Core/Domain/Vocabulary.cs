namespace AgentPortifolio.Core.Domain;

/// <summary>Stable machine-readable reasons. They are part of the audit contract.</summary>
public static class ReasonCodes
{
    public const string Ok = "ok";
    public const string ArgumentsInvalid = "arguments_invalid";
    public const string OperationNotAllowlisted = "operation_not_allowlisted";
    public const string ResourceNotCanonical = "resource_not_canonical";
    public const string AgentUnknown = "agent_unknown";
    public const string AgentNotActive = "agent_not_active";
    public const string AgentSuspended = "agent_suspended";
    public const string WalletExpired = "wallet_expired";
    public const string CapabilityMissing = "capability_missing";
    public const string CapabilityExpired = "capability_expired";
    public const string CapabilityRevoked = "capability_revoked";
    public const string CapabilityStaleEpoch = "capability_stale_epoch";
    public const string RateLimited = "rate_limited";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string IdempotentReplay = "idempotent_replay";
    public const string InProgress = "in_progress";
    public const string StateUnavailable = "state_unavailable";
    public const string RevalidationFailed = "revalidation_failed";
    public const string ExternalTimeout = "external_timeout";
    public const string ExternalThrottled = "external_throttled";
    public const string ExternalDenied = "external_denied";
    public const string ExternalErrorUnknown = "external_error_unknown";
    public const string ResultNotRecorded = "result_not_recorded";
    public const string StaleExecution = "stale_execution";
}

public static class EventTypes
{
    public const string AgentRegistered = "agent.registered";
    public const string AgentSubmitted = "agent.submitted_for_review";
    public const string AgentActivated = "agent.activated";
    public const string AgentSuspended = "agent.suspended";
    public const string AgentRetired = "agent.retired";
    public const string ReactivationRequested = "agent.reactivation_requested";
    public const string AgentReactivated = "agent.reactivated";
    public const string CapabilityGranted = "capability.granted";
    public const string CapabilityRevoked = "capability.revoked";
    public const string GrantRecorded = "grant.recorded";
    public const string GrantAbsent = "grant.absent";
    public const string CoverageChanged = "coverage.changed";
    public const string ActionDenied = "action.denied";
    public const string ActionAdmitted = "action.admitted";
    public const string ActionExecuting = "action.executing";
    public const string ActionCancelled = "action.cancelled_at_revalidation";
    public const string ActionCompleted = "action.completed";
    public const string ContainmentStep = "containment.step";
    public const string ContainmentCompleted = "containment.completed";
    public const string ContainmentSuperseded = "containment.superseded";
    public const string ContainmentDeadLettered = "containment.dead_lettered";
    public const string AlertPostSuspensionAttempt = "alert.post_suspension_attempt";
    public const string AlertDrift = "alert.drift_detected";
    public const string AlertBroadGrant = "alert.broad_grant";
    public const string AlertDirectAccess = "alert.direct_access_discovered";
}

public static class Roles
{
    public const string Reader = "AgentPortifolio.Reader";
    public const string IncidentOperator = "AgentPortifolio.IncidentOperator";
    public const string PolicyAdmin = "AgentPortifolio.PolicyAdmin";
    public const string Auditor = "AgentPortifolio.Auditor";
}

public static class OutboxTypes
{
    public const string ContainmentRequested = "containment.requested";
}

public static class AccessModes
{
    public const string Application = "application";
    public const string Operator = "operator";
    public const string System = "system";
}
