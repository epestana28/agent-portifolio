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
microsoft.AddDocument(approved, "01ITEM", "politica-de-ferias.txt", "Texto do documento aprovado.");
microsoft.AddDocument(other, "01ITEM", "salarios.txt", "Documento de outra biblioteca.");

Console.WriteLine("Agent Portifolio, fatia vertical local. Lado Microsoft: SIMULADO.");

Step("1. Registar o agente, com dono, identidade própria e identidade de executor separada");
var agentIdentity = Guid.NewGuid();
var executorIdentity = Guid.NewGuid();
microsoft.AddIdentity(agentIdentity);
microsoft.AddIdentity(executorIdentity);
microsoft.AddRuntime(agentIdentity);
microsoft.IssueToken(agentIdentity, time.GetUtcNow().AddMinutes(60));
var externalGrant = microsoft.AddGrant(executorIdentity, "Sites.Selected", approved.Canonical, "read");
var agent = await control.RegisterAsync(adminA, new RegisterAgentCommand(IdentityType.EntraAgentId, agentIdentity, null, executorIdentity,
    "container-app/pilot", "consultar documentos aprovados", Guid.NewGuid(), null, "owner@example.invalid", 100, time.GetUtcNow().AddDays(30)), Corr(), default);
Show("estado", agent.State);

Step("2. Activar exige duas pessoas");
await control.SubmitForReviewAsync(adminA, agent.Id, Corr(), default);
try
{
    await control.ApproveAsync(adminA, agent.Id, Corr(), default);
}
catch (ForbiddenException ex)
{
    Show("mesma pessoa a aprovar", $"recusado ({ex.Message})");
}

agent = await control.ApproveAsync(adminB, agent.Id, Corr(), default);
Show("segunda pessoa a aprovar", agent.State);

Step("3. Carteira: uma capacidade, uma biblioteca, com validade");
var capability = await control.GrantCapabilityAsync(adminA, agent.Id,
    new GrantCapabilityCommand(OperationCatalog.DocumentRead, approved.Canonical, time.GetUtcNow().AddDays(7)), Corr(), default);
await control.RecordGrantAsync(adminA, agent.Id,
    new RecordGrantCommand(executorIdentity, externalGrant.Type, externalGrant.ExternalId, externalGrant.Resource, externalGrant.Role, "demo"), Corr(), default);
Show("capacidade", $"{capability.Action} em {capability.Resource}");
Show("epoch de revogação", agent.RevocationEpoch);

Step("4. O agente lê um documento da biblioteca aprovada");
var caller = new CallerIdentity(tenant, agentIdentity);
var read = await execution.ExecuteAsync(caller, Read(approved), Corr(), default);
Show("resultado", $"{read.Status}, {read.ReasonCode}");
if (read.Payload is DocumentContent document)
{
    Show("conteúdo devolvido ao agente", Encoding.UTF8.GetString(document.Bytes));
}

Step("5. O mesmo agente tenta outra biblioteca e uma operação fora da lista");
Show("outra biblioteca", Outcome(await execution.ExecuteAsync(caller, Read(other), Corr(), default)));
Show("operação inventada", Outcome(await execution.ExecuteAsync(caller,
    new ExecutionRequest("graph.batch", new Dictionary<string, string> { ["requests"] = "[]" }, Key()), Corr(), default)));

Step("6. Incidente: suspender. Uma transacção: estado, epoch, operação de contenção, outbox, auditoria");
var operation = await control.SuspendAsync(incident, agent.Id, "comportamento anómalo", Corr(), default);
Show("estado da contenção", operation.State);
Show("epoch depois", operation.EpochAfter);

Step("7. Nova tentativa do agente, antes de qualquer passo externo");
Show("leitura", Outcome(await execution.ExecuteAsync(caller, Read(approved), Corr(), default)));

Step("8. Contenção externa (SIMULADA): desactivar identidade, remover grants, isolar runtime, sondar tokens emitidos");
await containment.ProcessPendingAsync(default);
operation = (await control.ListContainmentsAsync(auditor, agent.Id, default)).Single();
foreach (var step in operation.Steps)
{
    Show(step.Name, $"{step.Status}{(step.Limitation is null ? string.Empty : $" | limitação: {step.Limitation}")}");
}

Show("resumo", operation.State);
foreach (var uncertainty in operation.Uncertainties)
{
    Show("incerteza declarada", uncertainty);
}

Step("9. Reactivar: pedido de uma pessoa, aprovação de outra, grants revalidados, capacidades antigas mortas");
await control.RequestReactivationAsync(adminA, agent.Id, Corr(), default);
try
{
    await control.ApproveReactivationAsync(adminB, agent.Id, Corr(), default);
}
catch (InvalidTransitionException ex)
{
    Show("aprovar sem revalidar grants", $"recusado ({ex.ReasonCode})");
}

await reconciliation.ReconcileAsync(tenant, agent.Id, Corr(), default);
try
{
    agent = await control.ApproveReactivationAsync(adminB, agent.Id, Corr(), default);
    Show("estado", $"{agent.State}, epoch {agent.RevocationEpoch}");
    Show("leitura com a capacidade antiga", Outcome(await execution.ExecuteAsync(caller, Read(approved), Corr(), default)));
}
catch (InvalidTransitionException ex)
{
    Show("aprovar depois de reconciliar", $"recusado ({ex.ReasonCode})");
}

Step("10. Registo de auditoria do agente (com SQL Server: tabela ledger append-only)");
foreach (var e in await control.ReadAuditAsync(auditor, agent.Id, default))
{
    Console.WriteLine($"   {e.OccurredAt:HH:mm:ss.fff}  {e.EventType,-30} {e.Decision,-6} {e.ReasonCode,-26} epoch={e.RevocationEpoch}");
}

Console.WriteLine();
Console.WriteLine("Fim. Tudo o que diz respeito a Entra, Graph e SharePoint foi simulado.");
Console.WriteLine("O bloqueio local, a autorização e a auditoria são o código real do produto.");

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
