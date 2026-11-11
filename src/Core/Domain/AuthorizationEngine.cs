namespace AgentPortifolio.Core.Domain;

public sealed record AuthorizationResult(bool Allowed, string ReasonCode, Capability? Capability)
{
    public static AuthorizationResult Deny(string reasonCode) => new(false, reasonCode, null);
}

/// <summary>
/// Deterministic authorisation. Pure functions over state read inside one
/// transaction: no clock of its own, no I/O, no cache and no model in the loop.
/// The product can only narrow what the external grants allow, never widen it.
/// </summary>
public static class AuthorizationEngine
{
    public static AuthorizationResult Evaluate(
        Agent agent,
        IReadOnlyList<Capability> capabilities,
        ResolvedOperation operation,
        int actionsInLastHour,
        DateTimeOffset now)
    {
        if (agent.State != AgentState.Active)
        {
            return AuthorizationResult.Deny(agent.State == AgentState.Suspended ? ReasonCodes.AgentSuspended : ReasonCodes.AgentNotActive);
        }

        if (agent.WalletExpiresAt <= now)
        {
            return AuthorizationResult.Deny(ReasonCodes.WalletExpired);
        }

        var candidates = capabilities
            .Where(c => c.TenantId == agent.TenantId && c.AgentId == agent.Id
                && string.Equals(c.Action, operation.Operation, StringComparison.Ordinal)
                && string.Equals(c.Resource, operation.CanonicalResource, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0)
        {
            return AuthorizationResult.Deny(ReasonCodes.CapabilityMissing);
        }

        var valid = candidates.FirstOrDefault(c => CapabilityProblem(c, agent, now) is null);
        if (valid is null)
        {
            return AuthorizationResult.Deny(CapabilityProblem(candidates[^1], agent, now)!);
        }

        if (actionsInLastHour >= agent.Limits.MaxActionsPerHour)
        {
            return AuthorizationResult.Deny(ReasonCodes.RateLimited);
        }

        return new AuthorizationResult(true, ReasonCodes.Ok, valid);
    }

    /// <summary>Second check, run immediately before the external effect.</summary>
    public static string? Revalidate(Agent? agent, Capability? capability, ActionRecord action, DateTimeOffset now)
    {
        if (agent is null || capability is null)
        {
            return ReasonCodes.RevalidationFailed;
        }

        if (agent.State != AgentState.Active)
        {
            return agent.State == AgentState.Suspended ? ReasonCodes.AgentSuspended : ReasonCodes.AgentNotActive;
        }

        if (agent.RevocationEpoch != action.RevocationEpoch)
        {
            return ReasonCodes.CapabilityStaleEpoch;
        }

        return agent.WalletExpiresAt <= now ? ReasonCodes.WalletExpired : CapabilityProblem(capability, agent, now);
    }

    private static string? CapabilityProblem(Capability capability, Agent agent, DateTimeOffset now)
    {
        if (capability.RevokedAt is not null)
        {
            return ReasonCodes.CapabilityRevoked;
        }

        if (capability.IssuedEpoch != agent.RevocationEpoch)
        {
            return ReasonCodes.CapabilityStaleEpoch;
        }

        return capability.ExpiresAt <= now ? ReasonCodes.CapabilityExpired : null;
    }
}
