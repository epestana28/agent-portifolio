using System.Text;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Core.Storage;
using AgentPortifolio.Simulation;

// Local vertical slice, in one process, with no credentials:
// agent -> wallet -> authorisation -> action -> audit -> suspension -> refusal -> containment report.
//
// The Microsoft side (Entra, Graph, SharePoint) is SIMULATED. This run shows the
// control logic of the product. It is not evidence of how Microsoft services behave.

var time = TimeProvider.System;
var store = new InMemoryWalletStore(time);
var microsoft = new SimulatedMicrosoftEnvironment();
var control = new ControlService(store, time);
var execution = new ExecutionService(store, new SimulatedDocumentGateway(microsoft), time, new ExecutionOptions());
var reconciliation = new ReconciliationService(store, new SimulatedGrantSource(microsoft), time);
IContainmentAdapter[] adapters =
[
    new SimulatedIdentityDisableAdapter(microsoft),
    new SimulatedGrantRemovalAdapter(microsoft),
    new SimulatedRuntimeIsolationAdapter(microsoft),
    new SimulatedIssuedTokenProbeAdapter(microsoft, time),
];
var containment = new ContainmentOrchestrator(store, adapters, time, new ContainmentOptions());

var tenant = Guid.NewGuid();
Operator Person(string role) => new(tenant, Guid.NewGuid(), new HashSet<string> { role });
var adminA = Person(Roles.PolicyAdmin);
var adminB = Person(Roles.PolicyAdmin);
var incident = Person(Roles.IncidentOperator);
var auditor = Person(Roles.Auditor);

var approved = new SharePointLibrary("contoso.sharepoint.com,11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222", "b!ApprovedDrive");
var other = new SharePointLibrary("contoso.sharepoint.com,33333333-3333-4333-8333-333333333333,44444444-4444-4444-8444-444444444444", "b!OtherDrive");
microsoft.AddDocument(approved, "01ITEM", "holiday-policy.txt", "Text of the approved document.");
microsoft.AddDocument(other, "01ITEM", "salaries.txt", "Document from another library.");

Console.WriteLine("Agent Portifolio, local vertical slice. Microsoft side: SIMULATED.");

Step("1. Register the agent, with an owner, its own identity and a separate executor identity");
var agentIdentity = Guid.NewGuid();
var executorIdentity = Guid.NewGuid();
microsoft.AddIdentity(agentIdentity);
microsoft.AddIdentity(executorIdentity);
microsoft.AddRuntime(agentIdentity);
microsoft.IssueToken(agentIdentity, time.GetUtcNow().AddMinutes(60));
var externalGrant = microsoft.AddGrant(executorIdentity, "Sites.Selected", approved.Canonical, "read");
var agent = await control.RegisterAsync(adminA, new RegisterAgentCommand(IdentityType.EntraAgentId, agentIdentity, null, executorIdentity,
    "container-app/pilot", "read_approved_documents", Guid.NewGuid(), null, "owner@example.invalid", 100, time.GetUtcNow().AddDays(30)), Corr(), default);
Show("state", agent.State);

Step("2. Activation needs two people");
await control.SubmitForReviewAsync(adminA, agent.Id, Corr(), default);
try
{
    await control.ApproveAsync(adminA, agent.Id, Corr(), default);
}
catch (ForbiddenException ex)
{
    Show("same person approving", $"refused ({ex.Message})");
}

agent = await control.ApproveAsync(adminB, agent.Id, Corr(), default);
Show("second person approving", agent.State);

Step("3. Wallet: one capability, one library, with an expiry");
var capability = await control.GrantCapabilityAsync(adminA, agent.Id,
    new GrantCapabilityCommand(OperationCatalog.DocumentRead, approved.Canonical, time.GetUtcNow().AddDays(7)), Corr(), default);
await control.RecordGrantAsync(adminA, agent.Id,
    new RecordGrantCommand(executorIdentity, externalGrant.Type, externalGrant.ExternalId, externalGrant.Resource, externalGrant.Role, "demo"), Corr(), default);
Show("capability", $"{capability.Action} on {capability.Resource}");
Show("revocation epoch", agent.RevocationEpoch);

Step("4. The agent reads a document from the approved library");
var caller = new CallerIdentity(tenant, agentIdentity);
var read = await execution.ExecuteAsync(caller, Read(approved), Corr(), default);
Show("result", $"{read.Status}, {read.ReasonCode}");
if (read.Payload is DocumentContent document)
{
    Show("content returned to the agent", Encoding.UTF8.GetString(document.Bytes));
}

Step("5. The same agent tries another library and an operation outside the allowlist");
Show("other library", Outcome(await execution.ExecuteAsync(caller, Read(other), Corr(), default)));
Show("invented operation", Outcome(await execution.ExecuteAsync(caller,
    new ExecutionRequest("graph.batch", new Dictionary<string, string> { ["requests"] = "[]" }, Key()), Corr(), default)));

Step("6. Incident: suspend. One transaction: state, epoch, containment operation, outbox, audit");
var operation = await control.SuspendAsync(incident, agent.Id, "anomalous behaviour", Corr(), default);
Show("containment state", operation.State);
Show("epoch after", operation.EpochAfter);

Step("7. New attempt by the agent, before any external step");
Show("read", Outcome(await execution.ExecuteAsync(caller, Read(approved), Corr(), default)));

Step("8. External containment (SIMULATED): disable identity, remove grants, isolate runtime, probe issued tokens");
await containment.ProcessPendingAsync(default);
operation = (await control.ListContainmentsAsync(auditor, agent.Id, default)).Single();
foreach (var step in operation.Steps)
{
    Show(step.Name, $"{step.Status}{(step.Limitation is null ? string.Empty : $" | limitation: {step.Limitation}")}");
}

Show("summary", operation.State);
foreach (var uncertainty in operation.Uncertainties)
{
    Show("declared uncertainty", uncertainty);
}

Step("9. Reactivate: request by one person, approval by another, grants revalidated, old capabilities dead");
await control.RequestReactivationAsync(adminA, agent.Id, Corr(), default);
try
{
    await control.ApproveReactivationAsync(adminB, agent.Id, Corr(), default);
}
catch (InvalidTransitionException ex)
{
    Show("approving without revalidating grants", $"refused ({ex.ReasonCode})");
}

await reconciliation.ReconcileAsync(tenant, agent.Id, Corr(), default);
try
{
    agent = await control.ApproveReactivationAsync(adminB, agent.Id, Corr(), default);
    Show("state", $"{agent.State}, epoch {agent.RevocationEpoch}");
    Show("read with the old capability", Outcome(await execution.ExecuteAsync(caller, Read(approved), Corr(), default)));
}
catch (InvalidTransitionException ex)
{
    Show("approving after reconciling", $"refused ({ex.ReasonCode})");
}

Step("10. Audit record of the agent (with SQL Server: append-only ledger table)");
foreach (var e in await control.ReadAuditAsync(auditor, agent.Id, default))
{
    Console.WriteLine($"   {e.OccurredAt:HH:mm:ss.fff}  {e.EventType,-30} {e.Decision,-6} {e.ReasonCode,-26} epoch={e.RevocationEpoch}");
}

Console.WriteLine();
Console.WriteLine("End. Everything that concerns Entra, Graph and SharePoint was simulated.");
Console.WriteLine("The local block, the authorisation and the audit are the real code of the product.");

static void Step(string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
}

static void Show(string label, object? value) => Console.WriteLine($"   {label}: {value}");

static string Corr() => Guid.NewGuid().ToString("N");

static string Key() => Guid.NewGuid().ToString("N");

static string Outcome(ExecutionOutcome outcome) => $"{outcome.Status}, {outcome.ReasonCode}";

static ExecutionRequest Read(SharePointLibrary library) =>
    new(OperationCatalog.DocumentRead, new Dictionary<string, string> { ["siteId"] = library.SiteId, ["driveId"] = library.DriveId, ["itemId"] = "01ITEM" }, Key());
