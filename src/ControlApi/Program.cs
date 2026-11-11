using System.Security.Claims;
using System.Text.Json.Serialization;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Hosting;

// Control plane for operators. Privileged, and deliberately a separate process
// and identity from the executor that touches data.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAgentPortifolioAuth(builder.Configuration, Audiences.Control);
builder.Services.AddWalletStore(builder.Configuration);
builder.Services.AddExternalAdapters(builder.Configuration);
builder.Services.AddSingleton(sp => new ControlService(sp.GetRequiredService<IWalletStore>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new ReconciliationService(sp.GetRequiredService<IWalletStore>(),
    sp.GetRequiredService<IExternalGrantSource>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var app = builder.Build();
await app.InitialiseAsync();
app.UseDomainErrors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// The tenant and the operator always come from the validated token.
var api = app.MapGroup("/").RequireAuthorization();
static string Correlation() => Guid.NewGuid().ToString("N");

api.MapGet("/agents", (ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.ListAgentsAsync(user.ToOperator(), ct));

api.MapPost("/agents", async (RegisterAgentCommand command, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
{
    var agent = await control.RegisterAsync(user.ToOperator(), command, Correlation(), ct);
    return Results.Created($"/agents/{agent.Id}", agent);
});

api.MapGet("/agents/{id:guid}", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.GetAgentAsync(user.ToOperator(), id, ct));

api.MapPost("/agents/{id:guid}/submit", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.SubmitForReviewAsync(user.ToOperator(), id, Correlation(), ct));

api.MapPost("/agents/{id:guid}/approve", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.ApproveAsync(user.ToOperator(), id, Correlation(), ct));

api.MapPost("/agents/{id:guid}/retire", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.RetireAsync(user.ToOperator(), id, Correlation(), ct));

api.MapPost("/agents/{id:guid}/capabilities", async (Guid id, GrantCapabilityCommand command, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
{
    var capability = await control.GrantCapabilityAsync(user.ToOperator(), id, command, Correlation(), ct);
    return Results.Created($"/agents/{id}/capabilities/{capability.Id}", capability);
});

api.MapDelete("/agents/{id:guid}/capabilities/{capabilityId:guid}", (Guid id, Guid capabilityId, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.RevokeCapabilityAsync(user.ToOperator(), id, capabilityId, Correlation(), ct));

api.MapPost("/agents/{id:guid}/grants", (Guid id, RecordGrantCommand command, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.RecordGrantAsync(user.ToOperator(), id, command, Correlation(), ct));

api.MapPost("/agents/{id:guid}/reconcile", (Guid id, ClaimsPrincipal user, ReconciliationService reconciliation, CancellationToken ct) =>
{
    var op = user.ToOperator();
    return op.HasAny(AgentPortifolio.Core.Domain.Roles.PolicyAdmin, AgentPortifolio.Core.Domain.Roles.IncidentOperator)
        ? reconciliation.ReconcileAsync(op.TenantId, id, Correlation(), ct)
        : throw new ForbiddenException("PolicyAdmin or IncidentOperator is required.");
});

api.MapPost("/agents/{id:guid}/suspend", (Guid id, SuspendRequest request, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.SuspendAsync(user.ToOperator(), id, request.Reason, Correlation(), ct));

api.MapPost("/agents/{id:guid}/reactivation-requests", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.RequestReactivationAsync(user.ToOperator(), id, Correlation(), ct));

api.MapPost("/agents/{id:guid}/reactivation-approvals", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.ApproveReactivationAsync(user.ToOperator(), id, Correlation(), ct));

api.MapGet("/agents/{id:guid}/containments", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.ListContainmentsAsync(user.ToOperator(), id, ct));

api.MapGet("/agents/{id:guid}/audit", (Guid id, ClaimsPrincipal user, ControlService control, CancellationToken ct) =>
    control.ReadAuditAsync(user.ToOperator(), id, ct));

app.Run();

public sealed record SuspendRequest(string Reason);

/// <summary>Anchor for test hosts.</summary>
public sealed class ControlApiMarker;
