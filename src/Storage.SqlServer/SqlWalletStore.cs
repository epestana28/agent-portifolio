using System.Data;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using Microsoft.Data.SqlClient;

namespace AgentPortifolio.Storage.SqlServer;

/// <summary>
/// Authoritative state on SQL Server 2022 or Azure SQL Database. State tables
/// keep indexed key columns plus the record as JSON; the audit trail is a typed
/// append-only ledger table. Any failure to reach or use the database surfaces
/// as <see cref="StoreUnavailableException"/> so callers fail closed.
/// </summary>
public sealed class SqlWalletStore(string connectionString) : IWalletStore
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>
    /// Creates the database when it does not exist. Meant for the local container stack;
    /// in Azure the database is created by infrastructure code and the service identity
    /// has no permission to do this.
    /// </summary>
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrEmpty(database))
        {
            throw new InvalidOperationException("The connection string names no database.");
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var name = "[" + database.Replace("]", "]]", StringComparison.Ordinal) + "]";
        await using var command = new SqlCommand(
            $"IF DB_ID(@name) IS NULL BEGIN CREATE DATABASE {name}; ALTER DATABASE {name} SET ALLOW_SNAPSHOT_ISOLATION ON; END", connection);
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgentPortifolio.Storage.SqlServer.Schema.001_schema.sql")
            ?? throw new InvalidOperationException("Embedded schema not found.");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(script, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T> TransactAsync<T>(Func<IWalletTx, Task<T>> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var result = await work(new Tx(connection, transaction));
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new ConcurrencyConflictException("A uniqueness rule was violated by a concurrent or repeated write.");
        }
        catch (SqlException ex)
        {
            throw new StoreUnavailableException("The authoritative store is unavailable.", ex);
        }
        catch (InvalidOperationException ex) when (ex.Source?.Contains("SqlClient", StringComparison.Ordinal) == true)
        {
            // Pool exhaustion and broken connections arrive as InvalidOperationException.
            throw new StoreUnavailableException("The authoritative store is unavailable.", ex);
        }
    }

    private sealed class Tx(SqlConnection connection, SqlTransaction transaction) : IWalletTx
    {
        // UPDLOCK: admission and suspension of one agent are serialised on this row.
        public Task<Agent?> GetAgentAsync(Guid tenantId, Guid agentId) =>
            OneAsync<Agent>("SELECT Doc FROM ap.Agent WITH (UPDLOCK, ROWLOCK) WHERE TenantId = @t AND Id = @id", ("@t", tenantId), ("@id", agentId));

        public Task<Agent?> FindAgentByIdentityAsync(Guid tenantId, Guid identityObjectId) =>
            OneAsync<Agent>("SELECT Doc FROM ap.Agent WHERE TenantId = @t AND IdentityObjectId = @o", ("@t", tenantId), ("@o", identityObjectId));

        public Task<IReadOnlyList<Agent>> ListAgentsAsync(Guid tenantId) =>
            ManyAsync<Agent>("SELECT Doc FROM ap.Agent WHERE TenantId = @t ORDER BY Id", ("@t", tenantId));

        public Task InsertAgentAsync(Agent agent) =>
            ExecAsync("INSERT ap.Agent (Id, TenantId, IdentityObjectId, State, RevocationEpoch, Version, Doc) VALUES (@id, @t, @o, @s, @e, @v, @d)",
                ("@id", agent.Id), ("@t", agent.TenantId), ("@o", agent.Identity.ObjectId), ("@s", agent.State.ToString()),
                ("@e", agent.RevocationEpoch), ("@v", agent.Version), ("@d", Serialize(agent)));

        public async Task<Agent> UpdateAgentAsync(Agent agent)
        {
            var updated = agent with { Version = agent.Version + 1 };
            var rows = await ExecAsync(
                "UPDATE ap.Agent SET State = @s, RevocationEpoch = @e, Version = @nv, Doc = @d WHERE TenantId = @t AND Id = @id AND Version = @v",
                ("@s", updated.State.ToString()), ("@e", updated.RevocationEpoch), ("@nv", updated.Version), ("@d", Serialize(updated)),
                ("@t", agent.TenantId), ("@id", agent.Id), ("@v", agent.Version));
            return rows == 1 ? updated : throw new ConcurrencyConflictException("Agent was changed by another transaction.");
        }

        public Task<Capability?> GetCapabilityAsync(Guid tenantId, Guid capabilityId) =>
            OneAsync<Capability>("SELECT Doc FROM ap.Capability WHERE TenantId = @t AND Id = @id", ("@t", tenantId), ("@id", capabilityId));

        public Task<IReadOnlyList<Capability>> ListCapabilitiesAsync(Guid tenantId, Guid agentId) =>
            ManyAsync<Capability>("SELECT Doc FROM ap.Capability WHERE TenantId = @t AND AgentId = @a ORDER BY IssuedAt, Id", ("@t", tenantId), ("@a", agentId));

        public Task InsertCapabilityAsync(Capability capability) =>
            ExecAsync("INSERT ap.Capability (Id, TenantId, AgentId, IssuedAt, Doc) VALUES (@id, @t, @a, @i, @d)",
                ("@id", capability.Id), ("@t", capability.TenantId), ("@a", capability.AgentId), ("@i", capability.IssuedAt), ("@d", Serialize(capability)));

        public Task UpdateCapabilityAsync(Capability capability) =>
            ExecAsync("UPDATE ap.Capability SET Doc = @d WHERE TenantId = @t AND Id = @id",
                ("@d", Serialize(capability)), ("@t", capability.TenantId), ("@id", capability.Id));

        public Task<IReadOnlyList<ExternalGrant>> ListGrantsAsync(Guid tenantId, Guid agentId) =>
            ManyAsync<ExternalGrant>("SELECT Doc FROM ap.ExternalGrant WHERE TenantId = @t AND AgentId = @a ORDER BY ExternalId", ("@t", tenantId), ("@a", agentId));

        public Task UpsertGrantAsync(ExternalGrant grant) =>
            ExecAsync("""
                UPDATE ap.ExternalGrant SET Doc = @d, ExternalId = @x WHERE TenantId = @t AND Id = @id;
                IF @@ROWCOUNT = 0 INSERT ap.ExternalGrant (Id, TenantId, AgentId, ExternalId, Doc) VALUES (@id, @t, @a, @x, @d);
                """,
                ("@d", Serialize(grant)), ("@x", grant.ExternalId), ("@t", grant.TenantId), ("@id", grant.Id), ("@a", grant.AgentId));

        public Task<ActionRecord?> GetActionAsync(Guid tenantId, Guid actionId) =>
            OneAsync<ActionRecord>("SELECT Doc FROM ap.Action WHERE TenantId = @t AND Id = @id", ("@t", tenantId), ("@id", actionId));

        public Task<ActionRecord?> FindActionByIdempotencyKeyAsync(Guid tenantId, Guid agentId, string idempotencyKey) =>
            OneAsync<ActionRecord>("SELECT Doc FROM ap.Action WHERE TenantId = @t AND AgentId = @a AND IdempotencyKey = @k COLLATE Latin1_General_BIN2",
                ("@t", tenantId), ("@a", agentId), ("@k", idempotencyKey));

        public async Task<int> CountActionsSinceAsync(Guid tenantId, Guid agentId, DateTimeOffset since)
        {
            await using var command = Command("SELECT COUNT(*) FROM ap.Action WHERE TenantId = @t AND AgentId = @a AND AdmittedAt >= @s",
                ("@t", tenantId), ("@a", agentId), ("@s", since));
            return (int)(await command.ExecuteScalarAsync())!;
        }

        public Task<IReadOnlyList<ActionRecord>> ListActionsAsync(Guid tenantId, Guid agentId) =>
            ManyAsync<ActionRecord>("SELECT Doc FROM ap.Action WHERE TenantId = @t AND AgentId = @a ORDER BY AdmittedAt, Id", ("@t", tenantId), ("@a", agentId));

        public Task InsertActionAsync(ActionRecord action) =>
            ExecAsync("INSERT ap.Action (Id, TenantId, AgentId, IdempotencyKey, AdmittedAt, State, Doc) VALUES (@id, @t, @a, @k, @at, @s, @d)",
                ("@id", action.Id), ("@t", action.TenantId), ("@a", action.AgentId), ("@k", action.IdempotencyKey), ("@at", action.AdmittedAt),
                ("@s", action.State.ToString()), ("@d", Serialize(action)));

        public Task UpdateActionAsync(ActionRecord action) =>
            ExecAsync("UPDATE ap.Action SET State = @s, Doc = @d WHERE TenantId = @t AND Id = @id",
                ("@s", action.State.ToString()), ("@d", Serialize(action)), ("@t", action.TenantId), ("@id", action.Id));

        public Task<ContainmentOperation?> GetContainmentAsync(Guid tenantId, Guid operationId) =>
            OneAsync<ContainmentOperation>("SELECT Doc FROM ap.ContainmentOperation WITH (UPDLOCK, ROWLOCK) WHERE TenantId = @t AND Id = @id",
                ("@t", tenantId), ("@id", operationId));

        public Task<IReadOnlyList<ContainmentOperation>> ListContainmentsAsync(Guid tenantId, Guid agentId) =>
            ManyAsync<ContainmentOperation>("SELECT Doc FROM ap.ContainmentOperation WHERE TenantId = @t AND AgentId = @a ORDER BY RequestedAt, EpochAfter",
                ("@t", tenantId), ("@a", agentId));

        public Task InsertContainmentAsync(ContainmentOperation operation) =>
            ExecAsync("INSERT ap.ContainmentOperation (Id, TenantId, AgentId, RequestedAt, EpochAfter, Doc) VALUES (@id, @t, @a, @r, @e, @d)",
                ("@id", operation.Id), ("@t", operation.TenantId), ("@a", operation.AgentId), ("@r", operation.RequestedAt),
                ("@e", operation.EpochAfter), ("@d", Serialize(operation)));

        public Task UpdateContainmentAsync(ContainmentOperation operation) =>
            ExecAsync("UPDATE ap.ContainmentOperation SET Doc = @d WHERE TenantId = @t AND Id = @id",
                ("@d", Serialize(operation)), ("@t", operation.TenantId), ("@id", operation.Id));

        public Task AppendAuditAsync(AuditEvent e) =>
            ExecAsync("""
                INSERT ap.AuditEvent (EventId, SchemaVersion, TenantId, AgentId, OccurredAt, RecordedAt, EventType, CorrelationId, HumanInitiator,
                    CallerIdentity, ExecutorIdentity, AccessMode, PolicyVersion, RevocationEpoch, CapabilityId, Resource, Operation, Decision,
                    ReasonCode, ExecutionState, Attempt, IdempotencyKey, EvidenceRef)
                VALUES (@id, @sv, @t, @a, @oc, SYSDATETIMEOFFSET(), @et, @c, @h, @ci, @ei, @am, @pv, @re, @cap, @res, @op, @dec, @rc, @es, @att, @ik, @ev)
                """,
                ("@id", e.EventId), ("@sv", e.SchemaVersion), ("@t", e.TenantId), ("@a", e.AgentId), ("@oc", e.OccurredAt), ("@et", e.EventType),
                ("@c", e.CorrelationId), ("@h", e.HumanInitiator), ("@ci", e.CallerIdentity), ("@ei", e.ExecutorIdentity), ("@am", e.AccessMode),
                ("@pv", e.PolicyVersion), ("@re", e.RevocationEpoch), ("@cap", e.CapabilityId), ("@res", e.Resource), ("@op", e.Operation),
                ("@dec", e.Decision), ("@rc", e.ReasonCode), ("@es", e.ExecutionState), ("@att", e.Attempt), ("@ik", e.IdempotencyKey),
                ("@ev", e.EvidenceRef));

        public async Task<IReadOnlyList<AuditEvent>> ReadAuditAsync(Guid tenantId, Guid? agentId)
        {
            await using var command = Command("""
                SELECT EventId, SchemaVersion, TenantId, AgentId, OccurredAt, RecordedAt, EventType, CorrelationId, HumanInitiator, CallerIdentity,
                    ExecutorIdentity, AccessMode, PolicyVersion, RevocationEpoch, CapabilityId, Resource, Operation, Decision, ReasonCode,
                    ExecutionState, Attempt, IdempotencyKey, EvidenceRef
                FROM ap.AuditEvent
                WHERE TenantId = @t AND (@a IS NULL OR AgentId = @a)
                ORDER BY ledger_start_transaction_id, ledger_start_sequence_number
                """, ("@t", tenantId), ("@a", agentId));
            var events = new List<AuditEvent>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                T? Value<T>(int i) where T : struct => reader.IsDBNull(i) ? null : reader.GetFieldValue<T>(i);
                string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

                events.Add(new AuditEvent(reader.GetGuid(0), reader.GetInt32(1), reader.GetGuid(2), Value<Guid>(3),
                    reader.GetDateTimeOffset(4), reader.GetDateTimeOffset(5), reader.GetString(6), reader.GetString(7), Value<Guid>(8),
                    Value<Guid>(9), Value<Guid>(10), reader.GetString(11), Value<int>(12), Value<long>(13), Value<Guid>(14), Text(15),
                    Text(16), Text(17), Text(18), Text(19), Value<int>(20), Text(21), Text(22)));
            }

            return events;
        }

        public Task EnqueueOutboxAsync(OutboxMessage message) =>
            ExecAsync("INSERT ap.Outbox (Id, TenantId, CreatedAt, Epoch, ProcessedAt, Doc) VALUES (@id, @t, @c, @e, @p, @d)",
                ("@id", message.Id), ("@t", message.TenantId), ("@c", message.CreatedAt), ("@e", message.Epoch), ("@p", message.ProcessedAt),
                ("@d", Serialize(message)));

        // READPAST: two workers never pick the same pending message at the same time.
        public Task<IReadOnlyList<OutboxMessage>> ListPendingOutboxAsync(int max) =>
            ManyAsync<OutboxMessage>("SELECT TOP (@m) Doc FROM ap.Outbox WITH (UPDLOCK, READPAST) WHERE ProcessedAt IS NULL ORDER BY CreatedAt, Epoch", ("@m", max));

        public Task UpdateOutboxAsync(OutboxMessage message) =>
            ExecAsync("UPDATE ap.Outbox SET ProcessedAt = @p, Doc = @d WHERE Id = @id",
                ("@p", message.ProcessedAt), ("@d", Serialize(message)), ("@id", message.Id));

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

        private SqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
        {
            var command = new SqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            return command;
        }

        private async Task<int> ExecAsync(string sql, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(sql, parameters);
            return await command.ExecuteNonQueryAsync();
        }

        private async Task<T?> OneAsync<T>(string sql, params (string Name, object? Value)[] parameters) where T : class
        {
            await using var command = Command(sql, parameters);
            return await command.ExecuteScalarAsync() is string doc ? JsonSerializer.Deserialize<T>(doc, Json) : null;
        }

        private async Task<IReadOnlyList<T>> ManyAsync<T>(string sql, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(sql, parameters);
            var items = new List<T>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), Json)!);
            }

            return items;
        }
    }
}
