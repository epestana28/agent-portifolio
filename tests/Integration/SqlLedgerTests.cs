using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Simulation;
using AgentPortifolio.Storage.SqlServer;
using Microsoft.Data.SqlClient;

namespace AgentPortifolio.Tests.Integration;

/// <summary>
/// Runs only when AP_TEST_SQL holds a connection string to a SQL Server 2022
/// instance (see scripts/sql-local-up.sh). Skipped otherwise, visibly.
/// </summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public const string Variable = "AP_TEST_SQL";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"{Variable} is not set; SQL Server ledger tests were not run.";
        }
    }
}

/// <summary>One fresh database per test class run, on a real SQL Server engine with ledger.</summary>
public sealed class SqlDatabase : IAsyncLifetime
{
    private readonly string? server = Environment.GetEnvironmentVariable(SqlFactAttribute.Variable);
    private readonly string name = $"ap_test_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        await ExecuteAsync(server, $"CREATE DATABASE [{name}]; ALTER DATABASE [{name}] SET ALLOW_SNAPSHOT_ISOLATION ON;");
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        await new SqlWalletStore(ConnectionString).EnsureSchemaAsync(default);
        // Running the schema twice must be harmless.
        await new SqlWalletStore(ConnectionString).EnsureSchemaAsync(default);
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(server))
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync(server, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }

    public static async Task<object?> ExecuteAsync(string connectionString, string sql, params (string, object)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (parameterName, value) in parameters)
        {
            command.Parameters.AddWithValue(parameterName, value);
        }

        return await command.ExecuteScalarAsync();
    }
}

public sealed class SqlLedgerTests(SqlDatabase database) : IClassFixture<SqlDatabase>
{
    private TestWorld NewWorld() => new(new SqlWalletStore(database.ConnectionString));

    [SqlFact]
    public async Task Vertical_slice_runs_on_sql_server_and_BT17_a_restart_does_not_reactivate()
    {
        var world = NewWorld();
        var agent = await world.ActiveAgentAsync();

        Assert.Equal(ExecutionStatus.Succeeded, (await world.ReadAsync(agent)).Status);
        var operation = await world.Control.SuspendAsync(world.Incident, agent.Id, "incident", "test", default);
        Assert.Equal(ReasonCodes.AgentSuspended, (await world.ReadAsync(agent)).ReasonCode);

        // "Restart": brand new store, services and worker over the same database.
        // (A real restore or geo-failover test belongs to phase C.)
        var store = new SqlWalletStore(database.ConnectionString);
        var execution = new ExecutionService(store, world.Gateway, world.Time, new ExecutionOptions());
        var afterRestart = await execution.ExecuteAsync(world.Caller(agent), TestWorld.Read(TestWorld.Approved), "test", default);
        Assert.Equal(ReasonCodes.AgentSuspended, afterRestart.ReasonCode);

        world.Environment.RevocationReachesIssuedTokens = true;
        Assert.Equal(1, await new ContainmentOrchestrator(store, world.Adapters, world.Time, world.ContainmentOptions).ProcessPendingAsync(default));
        var stored = Assert.Single(await world.Control.ListContainmentsAsync(world.Auditor, agent.Id, default));
        Assert.Equal(operation.Id, stored.Id);
        Assert.Equal(ContainmentState.VerifiedScoped, stored.State);
        Assert.Equal(4, stored.Steps.Count);
        Assert.Equal(AgentState.Suspended, (await world.AgentAsync(agent.Id)).State);

        var audit = await world.AuditAsync(agent.Id);
        Assert.Contains(audit, e => e.EventType == EventTypes.AgentSuspended);
        Assert.True(audit.ToList().FindIndex(e => e.EventType == EventTypes.ActionAdmitted) < audit.ToList().FindIndex(e => e.EventType == EventTypes.ActionCompleted));
    }

    [SqlFact]
    public async Task BT16_Ledger_table_rejects_update_and_delete()
    {
        var world = NewWorld();
        var agent = await world.ActiveAgentAsync();
        await world.ReadAsync(agent);

        await Assert.ThrowsAsync<SqlException>(() => SqlDatabase.ExecuteAsync(database.ConnectionString,
            "UPDATE ap.AuditEvent SET Decision = N'allow' WHERE AgentId = @a", ("@a", agent.Id)));
        await Assert.ThrowsAsync<SqlException>(() => SqlDatabase.ExecuteAsync(database.ConnectionString,
            "DELETE ap.AuditEvent WHERE AgentId = @a", ("@a", agent.Id)));
        await Assert.ThrowsAsync<SqlException>(() => SqlDatabase.ExecuteAsync(database.ConnectionString, "TRUNCATE TABLE ap.AuditEvent"));
        Assert.NotEmpty(await world.AuditAsync(agent.Id));
    }

    [SqlFact]
    public async Task BT16_Ledger_verifies_against_its_digest_and_a_wrong_digest_is_detected()
    {
        var world = NewWorld();
        var agent = await world.ActiveAgentAsync();
        await world.ReadAsync(agent);

        var digest = (string)(await SqlDatabase.ExecuteAsync(database.ConnectionString, "EXECUTE sys.sp_generate_database_ledger_digest"))!;
        await SqlDatabase.ExecuteAsync(database.ConnectionString, "EXECUTE sys.sp_verify_database_ledger @digests = @d", ("@d", digest));

        // A digest that does not match the stored chain must fail verification. This shows
        // the detection path; editing data files underneath the engine is a phase C drill.
        var hashStart = digest.IndexOf("0x", StringComparison.Ordinal) + 2;
        var forged = digest[..hashStart] + (digest[hashStart] == 'A' ? 'B' : 'A') + digest[(hashStart + 1)..];
        await Assert.ThrowsAsync<SqlException>(() => SqlDatabase.ExecuteAsync(database.ConnectionString,
            "EXECUTE sys.sp_verify_database_ledger @digests = @d", ("@d", forged)));
    }

    [SqlFact]
    public async Task Concurrent_admissions_respect_the_limit_and_idempotency_under_real_locking()
    {
        var world = NewWorld();
        var agent = await world.ActiveAgentAsync(maxActionsPerHour: 5);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => world.ReadAsync(agent))));
        Assert.Equal(5, outcomes.Count(o => o.Status == ExecutionStatus.Succeeded));
        Assert.Equal(15, outcomes.Count(o => o.ReasonCode == ReasonCodes.RateLimited));

        var other = await world.ActiveAgentAsync();
        var key = TestWorld.NewKey();
        var before = world.Environment.CallCount(SimulatedDocumentGateway.Target);
        var sameKey = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => world.ReadAsync(other, key: key))));
        Assert.Equal(1, world.Environment.CallCount(SimulatedDocumentGateway.Target) - before);
        Assert.Single(sameKey, o => !o.Replayed);
    }

    [SqlFact]
    public async Task Unreachable_database_fails_closed()
    {
        var unreachable = new SqlConnectionStringBuilder(database.ConnectionString) { DataSource = "127.0.0.1,1", ConnectTimeout = 2 }.ConnectionString;
        var world = new TestWorld(new SqlWalletStore(unreachable));
        var caller = new CallerIdentity(Guid.NewGuid(), Guid.NewGuid());

        var outcome = await world.Execution.ExecuteAsync(caller, TestWorld.Read(TestWorld.Approved), "test", default);

        Assert.Equal(ExecutionStatus.Denied, outcome.Status);
        Assert.Equal(ReasonCodes.StateUnavailable, outcome.ReasonCode);
    }
}
