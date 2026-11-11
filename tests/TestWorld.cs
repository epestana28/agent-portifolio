using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Core.Storage;
using AgentPortifolio.Simulation;

namespace AgentPortifolio.Tests;

public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>
/// One tenant, the domain services, and the SIMULATED Microsoft side. Tests that
/// use it prove the control logic, not the behaviour of Entra or Graph.
/// </summary>
public sealed class TestWorld
{
    public static readonly SharePointLibrary Approved = new("contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222", "b!ApprovedDrive");
    public static readonly SharePointLibrary Other = new("contoso.sharepoint.com,33333333-3333-4333-8333-333333333333,44444444-4444-4444-8444-444444444444", "b!OtherDrive");
    public const string ItemId = "01ITEM";
    public const string SecretText = "CONFIDENTIAL-DOCUMENT-BODY";

    public TestWorld(IWalletStore? store = null, IDocumentGateway? gateway = null)
    {
        Memory = new InMemoryWalletStore(Time);
        Store = store ?? Memory;
        Gateway = gateway ?? new SimulatedDocumentGateway(Environment);
        Control = new ControlService(Store, Time);
        Execution = new ExecutionService(Store, Gateway, Time, new ExecutionOptions { ExternalTimeout = TimeSpan.FromMilliseconds(1500) });
        ContainmentOptions = new ContainmentOptions
        {
            AttemptTimeout = TimeSpan.FromMilliseconds(200),
            MaxRetryAfter = TimeSpan.FromSeconds(5),
            Delay = (delay, _) =>
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            },
        };
        Adapters =
        [
            new SimulatedIdentityDisableAdapter(Environment),
            new SimulatedGrantRemovalAdapter(Environment),
            new SimulatedRuntimeIsolationAdapter(Environment),
            new SimulatedIssuedTokenProbeAdapter(Environment, Time),
        ];
        Containment = new ContainmentOrchestrator(Store, Adapters, Time, ContainmentOptions);
        Reconciliation = new ReconciliationService(Store, new SimulatedGrantSource(Environment), Time);

        Environment.AddDocument(Approved, ItemId, "approved.txt", SecretText);
        Environment.AddDocument(Other, ItemId, "other.txt", "OTHER-LIBRARY-BODY");
    }

    public ManualTime Time { get; } = new(new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero));
    public InMemoryWalletStore Memory { get; }
    public IWalletStore Store { get; }
    public SimulatedMicrosoftEnvironment Environment { get; } = new();
    public IDocumentGateway Gateway { get; }
    public ControlService Control { get; }
    public ExecutionService Execution { get; }
    public ContainmentOptions ContainmentOptions { get; }
    public List<IContainmentAdapter> Adapters { get; }
    public ContainmentOrchestrator Containment { get; }
    public ReconciliationService Reconciliation { get; }
    public List<TimeSpan> Delays { get; } = [];
    public Guid Tenant { get; } = Guid.NewGuid();

    public Operator AdminA => Op(adminA, Roles.PolicyAdmin);
    public Operator AdminB => Op(adminB, Roles.PolicyAdmin);
    public Operator Incident => Op(incident, Roles.IncidentOperator);
    public Operator Auditor => Op(auditor, Roles.Auditor);
    public Operator ReaderOnly => Op(reader, Roles.Reader);

    private readonly Guid adminA = Guid.NewGuid();
    private readonly Guid adminB = Guid.NewGuid();
    private readonly Guid incident = Guid.NewGuid();
    private readonly Guid auditor = Guid.NewGuid();
    private readonly Guid reader = Guid.NewGuid();

    public Operator Op(Guid objectId, params string[] roles) => new(Tenant, objectId, roles.ToHashSet());

    /// <summary>An approved, active agent with read and list on the approved library only.</summary>
    public async Task<Agent> ActiveAgentAsync(Guid? blueprintId = null, int maxActionsPerHour = 100)
    {
        var identity = Guid.NewGuid();
        var executor = Guid.NewGuid();
        Environment.AddIdentity(identity);
        Environment.AddIdentity(executor);
        Environment.AddRuntime(identity);
        var external = Environment.AddGrant(executor, "Sites.Selected", Approved.Canonical, "read");

        var agent = await Control.RegisterAsync(AdminA, new RegisterAgentCommand(IdentityType.EntraAgentId, identity, blueprintId, executor,
            "container-app/pilot", "consultar_documentos_aprovados", Guid.NewGuid(), null, "owner@example.invalid", maxActionsPerHour,
            Time.GetUtcNow().AddDays(30)), "test", default);
        await Control.SubmitForReviewAsync(AdminA, agent.Id, "test", default);
        await Control.ApproveAsync(AdminB, agent.Id, "test", default);
        await GrantAsync(agent.Id, OperationCatalog.DocumentRead, Approved);
        await GrantAsync(agent.Id, OperationCatalog.DocumentList, Approved);
        await Control.RecordGrantAsync(AdminA, agent.Id, new RecordGrantCommand(executor, external.Type, external.ExternalId, external.Resource, external.Role, "test"), "test", default);
        return (await Control.GetAgentAsync(AdminA, agent.Id, default)).Agent;
    }

    public Task<Capability> GrantAsync(Guid agentId, string action, SharePointLibrary library) =>
        Control.GrantCapabilityAsync(AdminA, agentId, new GrantCapabilityCommand(action, library.Canonical, Time.GetUtcNow().AddDays(7)), "test", default);

    public CallerIdentity Caller(Agent agent) => new(agent.TenantId, agent.Identity.ObjectId);

    public static ExecutionRequest Read(SharePointLibrary library, string? key = null, string itemId = ItemId) =>
        new(OperationCatalog.DocumentRead, new Dictionary<string, string> { ["siteId"] = library.SiteId, ["driveId"] = library.DriveId, ["itemId"] = itemId },
            key ?? NewKey());

    public static ExecutionRequest List(SharePointLibrary library, string? key = null) =>
        new(OperationCatalog.DocumentList, new Dictionary<string, string> { ["siteId"] = library.SiteId, ["driveId"] = library.DriveId }, key ?? NewKey());

    public static string NewKey() => Guid.NewGuid().ToString("N");

    public Task<ExecutionOutcome> ReadAsync(Agent agent, SharePointLibrary? library = null, string? key = null) =>
        Execution.ExecuteAsync(Caller(agent), Read(library ?? Approved, key), "test", default);

    public async Task<Agent> AgentAsync(Guid agentId) => (await Control.GetAgentAsync(AdminA, agentId, default)).Agent;

    public Task<IReadOnlyList<AuditEvent>> AuditAsync(Guid agentId) => Control.ReadAuditAsync(Auditor, agentId, default);

    public Task<IReadOnlyList<ActionRecord>> ActionsAsync(Agent agent) => Store.TransactAsync(tx => tx.ListActionsAsync(agent.TenantId, agent.Id), default);
}
