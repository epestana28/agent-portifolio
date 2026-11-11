using System.Collections.Immutable;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Core.Storage;

/// <summary>
/// LOCAL STAND-IN, not for production. Keeps the same transactional contract as
/// the SQL store (serialised transactions, all-or-nothing commit) so the domain
/// rules can be tested without infrastructure. It gives no tamper evidence:
/// that property only exists with Azure SQL Ledger.
/// </summary>
public sealed class InMemoryWalletStore(TimeProvider time) : IWalletStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private State committed = new();

    /// <summary>Fault injection: when false every transaction fails as if SQL were down.</summary>
    public bool Available { get; set; } = true;

    public async Task<T> TransactAsync<T>(Func<IWalletTx, Task<T>> work, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            throw new StoreUnavailableException("In-memory store marked unavailable.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            // The collections are immutable, so a shallow copy is a full snapshot.
            var draft = committed.Snapshot();
            var result = await work(new Tx(draft, time));
            if (!Available)
            {
                throw new StoreUnavailableException("In-memory store became unavailable before commit.");
            }

            committed = draft;
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class State
    {
        public ImmutableDictionary<Guid, Agent> Agents = ImmutableDictionary<Guid, Agent>.Empty;
        public ImmutableDictionary<Guid, Capability> Capabilities = ImmutableDictionary<Guid, Capability>.Empty;
        public ImmutableDictionary<Guid, ExternalGrant> Grants = ImmutableDictionary<Guid, ExternalGrant>.Empty;
        public ImmutableDictionary<Guid, ActionRecord> Actions = ImmutableDictionary<Guid, ActionRecord>.Empty;
        public ImmutableDictionary<Guid, ContainmentOperation> Containments = ImmutableDictionary<Guid, ContainmentOperation>.Empty;
        public ImmutableDictionary<Guid, OutboxMessage> Outbox = ImmutableDictionary<Guid, OutboxMessage>.Empty;
        public ImmutableList<AuditEvent> Audit = ImmutableList<AuditEvent>.Empty;

        public State Snapshot() => (State)MemberwiseClone();
    }

    private sealed class Tx(State state, TimeProvider time) : IWalletTx
    {
        public Task<Agent?> GetAgentAsync(Guid tenantId, Guid agentId) =>
            Task.FromResult(state.Agents.TryGetValue(agentId, out var agent) && agent.TenantId == tenantId ? agent : null);

        public Task<Agent?> FindAgentByIdentityAsync(Guid tenantId, Guid identityObjectId) =>
            Task.FromResult(state.Agents.Values.FirstOrDefault(a => a.TenantId == tenantId && a.Identity.ObjectId == identityObjectId));

        public Task<IReadOnlyList<Agent>> ListAgentsAsync(Guid tenantId) =>
            Task.FromResult<IReadOnlyList<Agent>>(state.Agents.Values.Where(a => a.TenantId == tenantId).OrderBy(a => a.Id).ToList());

        public Task InsertAgentAsync(Agent agent)
        {
            if (state.Agents.Values.Any(a => a.TenantId == agent.TenantId && a.Identity.ObjectId == agent.Identity.ObjectId))
            {
                throw new ConcurrencyConflictException("An agent with this external identity already exists in the tenant.");
            }

            state.Agents = state.Agents.Add(agent.Id, agent);
            return Task.CompletedTask;
        }

        public Task<Agent> UpdateAgentAsync(Agent agent)
        {
            if (!state.Agents.TryGetValue(agent.Id, out var current) || current.TenantId != agent.TenantId || current.Version != agent.Version)
            {
                throw new ConcurrencyConflictException("Agent was changed by another transaction.");
            }

            var updated = agent with { Version = agent.Version + 1 };
            state.Agents = state.Agents.SetItem(agent.Id, updated);
            return Task.FromResult(updated);
        }

        public Task<Capability?> GetCapabilityAsync(Guid tenantId, Guid capabilityId) =>
            Task.FromResult(state.Capabilities.TryGetValue(capabilityId, out var c) && c.TenantId == tenantId ? c : null);

        public Task<IReadOnlyList<Capability>> ListCapabilitiesAsync(Guid tenantId, Guid agentId) =>
            Task.FromResult<IReadOnlyList<Capability>>(state.Capabilities.Values
                .Where(c => c.TenantId == tenantId && c.AgentId == agentId).OrderBy(c => c.IssuedAt).ThenBy(c => c.Id).ToList());

        public Task InsertCapabilityAsync(Capability capability)
        {
            state.Capabilities = state.Capabilities.Add(capability.Id, capability);
            return Task.CompletedTask;
        }

        public Task UpdateCapabilityAsync(Capability capability)
        {
            state.Capabilities = state.Capabilities.SetItem(capability.Id, capability);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExternalGrant>> ListGrantsAsync(Guid tenantId, Guid agentId) =>
            Task.FromResult<IReadOnlyList<ExternalGrant>>(state.Grants.Values
                .Where(g => g.TenantId == tenantId && g.AgentId == agentId).OrderBy(g => g.ExternalId, StringComparer.Ordinal).ToList());

        public Task UpsertGrantAsync(ExternalGrant grant)
        {
            state.Grants = state.Grants.SetItem(grant.Id, grant);
            return Task.CompletedTask;
        }

        public Task<ActionRecord?> GetActionAsync(Guid tenantId, Guid actionId) =>
            Task.FromResult(state.Actions.TryGetValue(actionId, out var a) && a.TenantId == tenantId ? a : null);

        public Task<ActionRecord?> FindActionByIdempotencyKeyAsync(Guid tenantId, Guid agentId, string idempotencyKey) =>
            Task.FromResult(state.Actions.Values.FirstOrDefault(a =>
                a.TenantId == tenantId && a.AgentId == agentId && string.Equals(a.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));

        public Task<int> CountActionsSinceAsync(Guid tenantId, Guid agentId, DateTimeOffset since) =>
            Task.FromResult(state.Actions.Values.Count(a => a.TenantId == tenantId && a.AgentId == agentId && a.AdmittedAt >= since));

        public Task<IReadOnlyList<ActionRecord>> ListActionsAsync(Guid tenantId, Guid agentId) =>
            Task.FromResult<IReadOnlyList<ActionRecord>>(state.Actions.Values
                .Where(a => a.TenantId == tenantId && a.AgentId == agentId).OrderBy(a => a.AdmittedAt).ThenBy(a => a.Id).ToList());

        public Task InsertActionAsync(ActionRecord action)
        {
            state.Actions = state.Actions.Add(action.Id, action);
            return Task.CompletedTask;
        }

        public Task UpdateActionAsync(ActionRecord action)
        {
            state.Actions = state.Actions.SetItem(action.Id, action);
            return Task.CompletedTask;
        }

        public Task<ContainmentOperation?> GetContainmentAsync(Guid tenantId, Guid operationId) =>
            Task.FromResult(state.Containments.TryGetValue(operationId, out var o) && o.TenantId == tenantId ? o : null);

        public Task<IReadOnlyList<ContainmentOperation>> ListContainmentsAsync(Guid tenantId, Guid agentId) =>
            Task.FromResult<IReadOnlyList<ContainmentOperation>>(state.Containments.Values
                .Where(o => o.TenantId == tenantId && o.AgentId == agentId).OrderBy(o => o.RequestedAt).ThenBy(o => o.EpochAfter).ToList());

        public Task InsertContainmentAsync(ContainmentOperation operation)
        {
            state.Containments = state.Containments.Add(operation.Id, operation);
            return Task.CompletedTask;
        }

        public Task UpdateContainmentAsync(ContainmentOperation operation)
        {
            state.Containments = state.Containments.SetItem(operation.Id, operation);
            return Task.CompletedTask;
        }

        public Task AppendAuditAsync(AuditEvent auditEvent)
        {
            state.Audit = state.Audit.Add(auditEvent with { RecordedAt = time.GetUtcNow() });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(Guid tenantId, Guid? agentId) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(state.Audit
                .Where(e => e.TenantId == tenantId && (agentId is null || e.AgentId == agentId)).ToList());

        public Task EnqueueOutboxAsync(OutboxMessage message)
        {
            state.Outbox = state.Outbox.Add(message.Id, message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> ListPendingOutboxAsync(int max) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(state.Outbox.Values
                .Where(m => m.ProcessedAt is null).OrderBy(m => m.CreatedAt).ThenBy(m => m.Epoch).Take(max).ToList());

        public Task UpdateOutboxAsync(OutboxMessage message)
        {
            state.Outbox = state.Outbox.SetItem(message.Id, message);
            return Task.CompletedTask;
        }
    }
}
