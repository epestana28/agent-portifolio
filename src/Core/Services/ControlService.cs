using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Core.Services;

public sealed record RegisterAgentCommand(
    IdentityType IdentityType,
    Guid IdentityObjectId,
    Guid? BlueprintId,
    Guid ExecutorObjectId,
    string Runtime,
    string Purpose,
    Guid OwnerObjectId,
    Guid? SponsorObjectId,
    string Contact,
    int MaxActionsPerHour,
    DateTimeOffset WalletExpiresAt);

public sealed record GrantCapabilityCommand(string Action, string Resource, DateTimeOffset ExpiresAt);

public sealed record RecordGrantCommand(Guid PrincipalObjectId, string Type, string ExternalId, string Resource, string Role, string Source);

/// <summary>
/// Privileged control plane: lifecycle of agents, wallets and capabilities.
/// Role checks live here and not only at the HTTP edge, so no host can forget them.
/// </summary>
public sealed class ControlService(IWalletStore store, TimeProvider time)
{
    public static readonly TimeSpan MaxCapabilityLifetime = TimeSpan.FromDays(90);

    public Task<Agent> RegisterAsync(Operator op, RegisterAgentCommand command, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        if (string.IsNullOrWhiteSpace(command.Purpose) || string.IsNullOrWhiteSpace(command.Runtime) || string.IsNullOrWhiteSpace(command.Contact))
        {
            throw new RequestValidationException("Purpose, runtime and contact are required.");
        }

        if (command.OwnerObjectId == Guid.Empty || command.IdentityObjectId == Guid.Empty || command.ExecutorObjectId == Guid.Empty)
        {
            throw new RequestValidationException("Owner, identity and executor identity are required. An agent without an accountable owner cannot be registered.");
        }

        if (command.ExecutorObjectId == command.IdentityObjectId)
        {
            throw new RequestValidationException("The executor identity must be distinct from the agent identity.");
        }

        if (command.MaxActionsPerHour <= 0)
        {
            throw new RequestValidationException("MaxActionsPerHour must be positive.");
        }

        return store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var agent = new Agent(Guid.NewGuid(), op.TenantId,
                new AgentIdentity(command.IdentityType, command.IdentityObjectId, command.BlueprintId), command.ExecutorObjectId,
                command.Runtime, command.Purpose, command.OwnerObjectId, command.SponsorObjectId, command.Contact, AgentState.Draft,
                PolicyVersion: 1, RevocationEpoch: 0, new WalletLimits(command.MaxActionsPerHour), command.WalletExpiresAt,
                new Coverage(CoverageMode.Unverified, false, null), ReviewRequestedBy: null, SuspendedAt: null, Version: 0);
            await tx.InsertAgentAsync(agent);
            await tx.AppendAuditAsync(OperatorEvent(now, op, agent, EventTypes.AgentRegistered, correlationId));
            return agent;
        }, ct);
    }

    public Task<Agent> SubmitForReviewAsync(Operator op, Guid agentId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return ChangeAsync(op, agentId, EventTypes.AgentSubmitted, correlationId, ct, agent =>
        {
            ExpectState(agent, AgentState.Draft);
            return agent with { State = AgentState.PendingReview, ReviewRequestedBy = op.ObjectId };
        });
    }

    /// <summary>Four-eyes: the approver cannot be the person who submitted.</summary>
    public Task<Agent> ApproveAsync(Operator op, Guid agentId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return ChangeAsync(op, agentId, EventTypes.AgentActivated, correlationId, ct, agent =>
        {
            ExpectState(agent, AgentState.PendingReview);
            RequireSecondPerson(op, agent);
            return agent with { State = AgentState.Active, ReviewRequestedBy = null };
        });
    }

    public Task<Agent> RetireAsync(Operator op, Guid agentId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return ChangeAsync(op, agentId, EventTypes.AgentRetired, correlationId, ct, agent =>
        {
            if (agent.State == AgentState.Retired)
            {
                throw new InvalidTransitionException("already_retired", "Agent is already retired.");
            }

            // Retiring also kills every capability issued so far.
            return agent with { State = AgentState.Retired, RevocationEpoch = agent.RevocationEpoch + 1, ReviewRequestedBy = null };
        });
    }

    public Task<Capability> GrantCapabilityAsync(Operator op, Guid agentId, GrantCapabilityCommand command, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        if (!OperationCatalog.IsAllowlisted(command.Action))
        {
            throw new RequestValidationException("Action is not in the operation allowlist.");
        }

        if (!ResourceCanonicalizer.TryParseCanonical(command.Resource, out _))
        {
            throw new RequestValidationException("Resource is not in canonical form.");
        }

        return store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            if (command.ExpiresAt <= now || command.ExpiresAt > now + MaxCapabilityLifetime)
            {
                throw new RequestValidationException("Capability expiry must be in the future and within the maximum lifetime.");
            }

            var agent = await LoadAsync(tx, op.TenantId, agentId);
            if (agent.State is AgentState.Retired or AgentState.Suspended)
            {
                throw new InvalidTransitionException("agent_not_grantable", "Capabilities cannot be granted to a suspended or retired agent.");
            }

            agent = await tx.UpdateAgentAsync(agent with { PolicyVersion = agent.PolicyVersion + 1 });
            var capability = new Capability(Guid.NewGuid(), agent.TenantId, agent.Id, command.Action, command.Resource, now,
                command.ExpiresAt, agent.RevocationEpoch, op.ObjectId, null);
            await tx.InsertCapabilityAsync(capability);
            await tx.AppendAuditAsync(OperatorEvent(now, op, agent, EventTypes.CapabilityGranted, correlationId,
                capability.Id, capability.Resource, capability.Action));
            return capability;
        }, ct);
    }

    /// <summary>Removes one capability. Other capabilities and the epoch stay untouched.</summary>
    public Task<Capability> RevokeCapabilityAsync(Operator op, Guid agentId, Guid capabilityId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.IncidentOperator, Roles.PolicyAdmin);
        return store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            var capability = await tx.GetCapabilityAsync(op.TenantId, capabilityId);
            if (capability is null || capability.AgentId != agent.Id)
            {
                throw new NotFoundException("Capability not found.");
            }

            if (capability.RevokedAt is not null)
            {
                return capability;
            }

            agent = await tx.UpdateAgentAsync(agent with { PolicyVersion = agent.PolicyVersion + 1 });
            var revoked = capability with { RevokedAt = now };
            await tx.UpdateCapabilityAsync(revoked);
            await tx.AppendAuditAsync(OperatorEvent(now, op, agent, EventTypes.CapabilityRevoked, correlationId,
                revoked.Id, revoked.Resource, revoked.Action));
            return revoked;
        }, ct);
    }

    /// <summary>
    /// Local block. One transaction: suspend, bump the epoch, persist the containment
    /// operation and the outbox message. When this returns, the block is durable,
    /// whether or not any message bus or external service is reachable.
    /// </summary>
    public Task<ContainmentOperation> SuspendAsync(Operator op, Guid agentId, string reason, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.IncidentOperator, Roles.PolicyAdmin);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RequestValidationException("A reason is required.");
        }

        return store.TransactAsync(async tx =>
        {
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            if (agent.State == AgentState.Suspended)
            {
                // Idempotent: a repeated request returns the operation already open for this epoch.
                var open = (await tx.ListContainmentsAsync(agent.TenantId, agent.Id)).LastOrDefault(o => o.EpochAfter == agent.RevocationEpoch);
                if (open is not null)
                {
                    return open;
                }
            }
            else if (agent.State != AgentState.Active)
            {
                throw new InvalidTransitionException("not_suspendable", $"An agent in state {agent.State} cannot be suspended.");
            }

            return await SuspendInTransactionAsync(tx, time.GetUtcNow(), agent, op.ObjectId, reason, correlationId);
        }, ct);
    }

    public Task<Agent> RequestReactivationAsync(Operator op, Guid agentId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return ChangeAsync(op, agentId, EventTypes.ReactivationRequested, correlationId, ct, agent =>
        {
            ExpectState(agent, AgentState.Suspended);
            return agent with { ReviewRequestedBy = op.ObjectId };
        });
    }

    /// <summary>
    /// Reactivation is a new activation, not an undo: second person, external grants
    /// re-verified after the suspension, new epoch and new policy version. Capabilities
    /// from before the suspension stay dead and must be granted again.
    /// </summary>
    public Task<Agent> ApproveReactivationAsync(Operator op, Guid agentId, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            ExpectState(agent, AgentState.Suspended);
            RequireSecondPerson(op, agent);

            var grants = await tx.ListGrantsAsync(agent.TenantId, agent.Id);
            if (grants.Any(g => g.LastVerifiedAt <= agent.SuspendedAt))
            {
                throw new InvalidTransitionException("grants_not_revalidated", "External grants must be re-verified after the suspension before reactivating.");
            }

            agent = await tx.UpdateAgentAsync(agent with
            {
                State = AgentState.Active,
                RevocationEpoch = agent.RevocationEpoch + 1,
                PolicyVersion = agent.PolicyVersion + 1,
                ReviewRequestedBy = null,
                SuspendedAt = null,
            });
            await tx.AppendAuditAsync(OperatorEvent(now, op, agent, EventTypes.AgentReactivated, correlationId));
            return agent;
        }, ct);
    }

    /// <summary>Records an external grant as verified now, by an operator who checked the source.</summary>
    public Task<ExternalGrant> RecordGrantAsync(Operator op, Guid agentId, RecordGrantCommand command, string correlationId, CancellationToken ct)
    {
        Require(op, Roles.PolicyAdmin);
        return store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            if (command.PrincipalObjectId != agent.ExecutorObjectId && command.PrincipalObjectId != agent.Identity.ObjectId)
            {
                throw new RequestValidationException("The grant principal must be the agent identity or its executor identity.");
            }

            var existing = (await tx.ListGrantsAsync(agent.TenantId, agent.Id))
                .FirstOrDefault(g => g.PrincipalObjectId == command.PrincipalObjectId && g.ExternalId == command.ExternalId);
            var grant = new ExternalGrant(existing?.Id ?? Guid.NewGuid(), agent.TenantId, agent.Id, command.PrincipalObjectId, command.Type,
                command.ExternalId, command.Resource, command.Role, command.Source, now, "present");
            await tx.UpsertGrantAsync(grant);
            await tx.AppendAuditAsync(OperatorEvent(now, op, agent, EventTypes.GrantRecorded, correlationId, resource: grant.Resource,
                evidenceRef: $"grant:{grant.ExternalId}"));
            return grant;
        }, ct);
    }

    public Task<IReadOnlyList<Agent>> ListAgentsAsync(Operator op, CancellationToken ct)
    {
        Require(op, Roles.Reader, Roles.IncidentOperator, Roles.PolicyAdmin, Roles.Auditor);
        return store.TransactAsync(tx => tx.ListAgentsAsync(op.TenantId), ct);
    }

    public Task<AgentView> GetAgentAsync(Operator op, Guid agentId, CancellationToken ct)
    {
        Require(op, Roles.Reader, Roles.IncidentOperator, Roles.PolicyAdmin, Roles.Auditor);
        return store.TransactAsync(async tx =>
        {
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            return new AgentView(agent, await tx.ListCapabilitiesAsync(agent.TenantId, agent.Id), await tx.ListGrantsAsync(agent.TenantId, agent.Id));
        }, ct);
    }

    public Task<IReadOnlyList<ContainmentOperation>> ListContainmentsAsync(Operator op, Guid agentId, CancellationToken ct)
    {
        Require(op, Roles.Reader, Roles.IncidentOperator, Roles.PolicyAdmin, Roles.Auditor);
        return store.TransactAsync(async tx =>
        {
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            return await tx.ListContainmentsAsync(agent.TenantId, agent.Id);
        }, ct);
    }

    public Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(Operator op, Guid? agentId, CancellationToken ct)
    {
        Require(op, Roles.Auditor);
        return store.TransactAsync(tx => tx.ReadAuditAsync(op.TenantId, agentId), ct);
    }

    /// <summary>Shared by the operator path and by automatic suspension on dangerous drift.</summary>
    internal static async Task<ContainmentOperation> SuspendInTransactionAsync(
        IWalletTx tx, DateTimeOffset now, Agent agent, Guid? initiator, string reason, string correlationId)
    {
        var suspended = await tx.UpdateAgentAsync(agent with
        {
            State = AgentState.Suspended,
            RevocationEpoch = agent.RevocationEpoch + 1,
            SuspendedAt = now,
            ReviewRequestedBy = null,
        });

        // Actions already past admission cannot be recalled. Name them instead of hiding them.
        var inFlight = (await tx.ListActionsAsync(agent.TenantId, agent.Id))
            .Where(a => a.State is ActionState.Admitted or ActionState.Executing)
            .Select(a => a.Id)
            .ToList();
        var uncertainties = new List<string>();
        if (inFlight.Count > 0)
        {
            uncertainties.Add($"{inFlight.Count} action(s) were already admitted when the block was persisted; see InFlightActionIds for their recorded outcome.");
        }

        var operation = new ContainmentOperation(Guid.NewGuid(), agent.TenantId, agent.Id, Scope: "local admission only",
            initiator, reason, ContainmentState.LocalBlocked, suspended.RevocationEpoch, now, now, null, [], inFlight, uncertainties);
        await tx.InsertContainmentAsync(operation);
        await tx.EnqueueOutboxAsync(new OutboxMessage(Guid.NewGuid(), agent.TenantId, OutboxTypes.ContainmentRequested, operation.Id,
            suspended.RevocationEpoch, now, null, 0, false));
        await tx.AppendAuditAsync(Audit.Create(now, agent.TenantId, agent.Id, EventTypes.AgentSuspended, correlationId,
            initiator is null ? AccessModes.System : AccessModes.Operator, humanInitiator: initiator,
            policyVersion: suspended.PolicyVersion, revocationEpoch: suspended.RevocationEpoch, reasonCode: "suspended",
            evidenceRef: $"containment:{operation.Id}"));
        return operation;
    }

    private Task<Agent> ChangeAsync(Operator op, Guid agentId, string eventType, string correlationId, CancellationToken ct, Func<Agent, Agent> change) =>
        store.TransactAsync(async tx =>
        {
            var agent = await LoadAsync(tx, op.TenantId, agentId);
            var updated = await tx.UpdateAgentAsync(change(agent));
            await tx.AppendAuditAsync(OperatorEvent(time.GetUtcNow(), op, updated, eventType, correlationId));
            return updated;
        }, ct);

    private static async Task<Agent> LoadAsync(IWalletTx tx, Guid tenantId, Guid agentId) =>
        await tx.GetAgentAsync(tenantId, agentId) ?? throw new NotFoundException("Agent not found.");

    private static void Require(Operator op, params string[] roles)
    {
        if (!op.HasAny(roles))
        {
            throw new ForbiddenException($"One of these roles is required: {string.Join(", ", roles)}.");
        }
    }

    private static void ExpectState(Agent agent, AgentState expected)
    {
        if (agent.State != expected)
        {
            throw new InvalidTransitionException("invalid_state", $"Agent is {agent.State}; expected {expected}.");
        }
    }

    private static void RequireSecondPerson(Operator op, Agent agent)
    {
        if (agent.ReviewRequestedBy is null)
        {
            throw new InvalidTransitionException("review_not_requested", "A review must be requested first.");
        }

        if (agent.ReviewRequestedBy == op.ObjectId)
        {
            throw new ForbiddenException("The approver must be a different person from the requester.");
        }
    }

    private static AuditEvent OperatorEvent(DateTimeOffset now, Operator op, Agent agent, string eventType, string correlationId,
        Guid? capabilityId = null, string? resource = null, string? operation = null, string? evidenceRef = null) =>
        Audit.Create(now, agent.TenantId, agent.Id, eventType, correlationId, AccessModes.Operator, humanInitiator: op.ObjectId,
            policyVersion: agent.PolicyVersion, revocationEpoch: agent.RevocationEpoch, capabilityId: capabilityId, resource: resource,
            operation: operation, evidenceRef: evidenceRef);
}

public sealed record AgentView(Agent Agent, IReadOnlyList<Capability> Capabilities, IReadOnlyList<ExternalGrant> Grants);
