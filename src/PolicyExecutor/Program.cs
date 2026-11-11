using System.Security.Claims;
using System.Text.Json.Serialization;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Hosting;

// Data plane. The only endpoint an agent can reach. It validates the caller's
// token itself, even behind a gateway, so the gateway cannot be bypassed.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAgentPortifolioAuth(builder.Configuration, Audiences.Executor);
builder.Services.AddWalletStore(builder.Configuration);
builder.Services.AddExternalAdapters(builder.Configuration);
builder.Services.AddSingleton(new ExecutionOptions
{
    ExternalTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Execution:ExternalTimeoutSeconds", 10)),
});
builder.Services.AddSingleton(sp => new ExecutionService(sp.GetRequiredService<IWalletStore>(), sp.GetRequiredService<IDocumentGateway>(),
    sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ExecutionOptions>()));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Unknown fields are rejected, not ignored.
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var app = builder.Build();
await app.InitialiseAsync();
app.UseDomainErrors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/actions", async (ExecutionRequest request, ClaimsPrincipal user, ExecutionService execution, HttpContext http, CancellationToken ct) =>
{
    var correlationId = Guid.NewGuid().ToString("N");
    http.Response.Headers["X-Correlation-Id"] = correlationId;
    http.Response.Headers[HostingExtensions.SimulationHeader] = "true";

    var outcome = await execution.ExecuteAsync(user.ToCaller(), request, correlationId, ct);
    object? result = outcome.Payload switch
    {
        DocumentContent d => new { d.Name, d.ContentType, ContentBase64 = Convert.ToBase64String(d.Bytes) },
        IReadOnlyList<DocumentEntry> list => new { Items = list },
        _ => null,
    };
    var body = new { outcome.ActionId, Status = outcome.Status, outcome.ReasonCode, outcome.Replayed, Result = result };

    var status = (outcome.Status, outcome.ReasonCode) switch
    {
        (ExecutionStatus.Succeeded, _) => StatusCodes.Status200OK,
        (ExecutionStatus.Denied, ReasonCodes.StateUnavailable) => StatusCodes.Status503ServiceUnavailable,
        (ExecutionStatus.Denied, ReasonCodes.IdempotencyConflict) => StatusCodes.Status409Conflict,
        (ExecutionStatus.Denied, ReasonCodes.ArgumentsInvalid or ReasonCodes.ResourceNotCanonical or ReasonCodes.OperationNotAllowlisted) => StatusCodes.Status400BadRequest,
        (ExecutionStatus.Denied, ReasonCodes.RateLimited) => StatusCodes.Status429TooManyRequests,
        (ExecutionStatus.Denied, _) => StatusCodes.Status403Forbidden,
        (ExecutionStatus.Cancelled, _) => StatusCodes.Status409Conflict,
        (ExecutionStatus.Failed, ReasonCodes.ResultNotRecorded) => StatusCodes.Status503ServiceUnavailable,
        (ExecutionStatus.Failed, _) => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status504GatewayTimeout,
    };
    return Results.Json(body, statusCode: status);
}).RequireAuthorization();

app.Run();

/// <summary>Anchor for test hosts.</summary>
public sealed class PolicyExecutorMarker;
