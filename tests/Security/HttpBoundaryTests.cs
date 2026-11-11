using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPortifolio.Tests.Security;

/// <summary>
/// The HTTP edge of both services, with real JWT validation (development key).
/// Covers blocking tests 2, 5, 6 and 19 as far as they can be shown without a
/// gateway and a tenant.
/// </summary>
public sealed class HttpBoundaryTests : IDisposable
{
    private const string Key = "test-signing-key-0123456789-abcdefghijklmnop";
    private readonly TestWorld world = new();
    private readonly ApiFactory<ControlApiMarker> control;
    private readonly ApiFactory<PolicyExecutorMarker> executor;

    public HttpBoundaryTests()
    {
        control = new ApiFactory<ControlApiMarker>(world);
        executor = new ApiFactory<PolicyExecutorMarker>(world);
    }

    public void Dispose()
    {
        control.Dispose();
        executor.Dispose();
    }

    [Fact]
    public async Task Agent_reads_through_http_and_is_refused_after_suspension()
    {
        var agent = await world.ActiveAgentAsync();
        var agentToken = AgentToken(agent);

        var before = await PostActionAsync(agentToken, ReadBody());
        var suspend = await SendAsync(control, HttpMethod.Post, $"/agents/{agent.Id}/suspend", OperatorToken(Roles.IncidentOperator), """{"reason":"incident"}""");
        var after = await PostActionAsync(agentToken, ReadBody());

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var body = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TestWorld.SecretText, Encoding.UTF8.GetString(Convert.FromBase64String(body.GetProperty("result").GetProperty("contentBase64").GetString()!)));
        Assert.Equal("true", before.Headers.GetValues(HostingExtensions.SimulationHeader).Single());
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal(ReasonCodes.AgentSuspended, (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task BT02_Forged_caller_and_forged_tenant_fail()
    {
        var agent = await world.ActiveAgentAsync();
        var forgedSignature = DevTokens.Create("another-key-another-key-another-key-0123", Audiences.Executor, agent.TenantId, agent.Identity.ObjectId, [], TimeSpan.FromMinutes(5));
        var foreignTenant = DevTokens.Create(Key, Audiences.Executor, Guid.NewGuid(), agent.Identity.ObjectId, [], TimeSpan.FromMinutes(5));
        var expired = DevTokens.Create(Key, Audiences.Executor, agent.TenantId, agent.Identity.ObjectId, [], TimeSpan.FromMinutes(5), DateTime.UtcNow.AddHours(-2));
        var unsigned = Unsigned(agent);
        var unknownIdentity = DevTokens.Create(Key, Audiences.Executor, agent.TenantId, Guid.NewGuid(), [], TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(forgedSignature, ReadBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(foreignTenant, ReadBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(expired, ReadBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(unsigned, ReadBody())).StatusCode);

        // A valid token of an identity that is no registered agent is authenticated but gets nothing.
        var unknown = await PostActionAsync(unknownIdentity, ReadBody());
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(0, world.Environment.CallCount(AgentPortifolio.Simulation.SimulatedDocumentGateway.Target));
    }

    [Fact]
    public async Task BT02_Identity_comes_from_the_token_never_from_the_body()
    {
        var victim = await world.ActiveAgentAsync();
        var attacker = await world.ActiveAgentAsync();
        await world.Control.SuspendAsync(world.Incident, attacker.Id, "incident", "test", default);
        var body = ReadBody().Replace("{\"operation\"", $"{{\"agent_id\":\"{victim.Id}\",\"operation\"", StringComparison.Ordinal);

        var response = await PostActionAsync(AgentToken(attacker), body);

        // Unknown fields are rejected outright.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await world.ActionsAsync(victim));
    }

    [Fact]
    public async Task BT05_Backend_cannot_be_used_without_a_token_or_with_a_token_for_the_other_audience()
    {
        var agent = await world.ActiveAgentAsync();
        var controlAudience = DevTokens.Create(Key, Audiences.Control, agent.TenantId, agent.Identity.ObjectId, [Roles.PolicyAdmin], TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(null, ReadBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostActionAsync(controlAudience, ReadBody())).StatusCode);
        // And an agent token opens nothing on the control plane.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(control, HttpMethod.Get, "/agents", AgentToken(agent), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(control, HttpMethod.Get, "/agents", null, null)).StatusCode);
    }

    [Fact]
    public async Task BT06_Executor_never_returns_a_credential_to_the_agent()
    {
        var agent = await world.ActiveAgentAsync();

        var response = await PostActionAsync(AgentToken(agent), ReadBody());
        var raw = await response.Content.ReadAsStringAsync();
        var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var marker in new[] { "access_token", "Bearer", "Authorization", "downloadUrl", "secret", agent.ExecutorObjectId.ToString() })
        {
            Assert.DoesNotContain(marker, raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(marker, headers, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BT19_Roles_are_enforced_on_the_control_api()
    {
        var agent = await world.ActiveAgentAsync();
        var grant = $$"""{"action":"{{OperationCatalog.DocumentRead}}","resource":"{{TestWorld.Other.Canonical}}","expiresAt":"{{DateTimeOffset.UtcNow.AddDays(1):O}}"}""";

        var readerSuspend = await SendAsync(control, HttpMethod.Post, $"/agents/{agent.Id}/suspend", OperatorToken(Roles.Reader), """{"reason":"x"}""");
        var noRoleSuspend = await SendAsync(control, HttpMethod.Post, $"/agents/{agent.Id}/suspend", OperatorToken(), """{"reason":"x"}""");
        var incidentGrant = await SendAsync(control, HttpMethod.Post, $"/agents/{agent.Id}/capabilities", OperatorToken(Roles.IncidentOperator), grant);
        var readerAudit = await SendAsync(control, HttpMethod.Get, $"/agents/{agent.Id}/audit", OperatorToken(Roles.Reader), null);
        var auditorAudit = await SendAsync(control, HttpMethod.Get, $"/agents/{agent.Id}/audit", OperatorToken(Roles.Auditor), null);

        Assert.Equal(HttpStatusCode.Forbidden, readerSuspend.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRoleSuspend.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, incidentGrant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readerAudit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, auditorAudit.StatusCode);
        Assert.Equal(AgentState.Active, (await world.AgentAsync(agent.Id)).State);
    }

    [Fact]
    public async Task Operator_of_another_tenant_cannot_see_or_touch_the_agent()
    {
        var agent = await world.ActiveAgentAsync();
        var otherTenant = Guid.NewGuid();
        using var otherControl = new ApiFactory<ControlApiMarker>(world, otherTenant);
        var token = DevTokens.Create(Key, Audiences.Control, otherTenant, Guid.NewGuid(), [Roles.PolicyAdmin, Roles.IncidentOperator], TimeSpan.FromMinutes(5));

        var get = await SendAsync(otherControl, HttpMethod.Get, $"/agents/{agent.Id}", token, null);
        var suspend = await SendAsync(otherControl, HttpMethod.Post, $"/agents/{agent.Id}/suspend", token, """{"reason":"x"}""");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, suspend.StatusCode);
        Assert.Equal(AgentState.Active, (await world.AgentAsync(agent.Id)).State);
    }

    [Theory]
    [InlineData("""{"operation":"graph.batch","arguments":{"requests":"[]"},"idempotencyKey":"abcdefgh12345678"}""")]
    [InlineData("""{"operation":"sharepoint.document.read","arguments":{"url":"https://graph.microsoft.com/v1.0/users"},"idempotencyKey":"abcdefgh12345678"}""")]
    [InlineData("""{"operation":"sharepoint.document.read","arguments":{"siteId":"x","driveId":"y","itemId":"../z"},"idempotencyKey":"abcdefgh12345678"}""")]
    public async Task Generic_proxy_batch_and_arbitrary_urls_are_impossible(string body)
    {
        var agent = await world.ActiveAgentAsync();

        var response = await PostActionAsync(AgentToken(agent), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, world.Environment.CallCount(AgentPortifolio.Simulation.SimulatedDocumentGateway.Target));
    }

    private string AgentToken(Agent agent) => DevTokens.Create(Key, Audiences.Executor, agent.TenantId, agent.Identity.ObjectId, [], TimeSpan.FromMinutes(5));

    private string OperatorToken(params string[] roles) => DevTokens.Create(Key, Audiences.Control, world.Tenant, Guid.NewGuid(), roles, TimeSpan.FromMinutes(5));

    private static string Unsigned(Agent agent)
    {
        static string Part(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = $$"""{"iss":"{{DevTokens.Issuer}}","aud":"{{Audiences.Executor}}","tid":"{{agent.TenantId}}","oid":"{{agent.Identity.ObjectId}}","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}""";
        return $"{Part("""{"alg":"none","typ":"JWT"}""")}.{Part(payload)}.";
    }

    private static string ReadBody() => JsonSerializer.Serialize(new
    {
        operation = OperationCatalog.DocumentRead,
        arguments = new { siteId = TestWorld.Approved.SiteId, driveId = TestWorld.Approved.DriveId, itemId = TestWorld.ItemId },
        idempotencyKey = TestWorld.NewKey(),
    });

    private Task<HttpResponseMessage> PostActionAsync(string? token, string body) => SendAsync(executor, HttpMethod.Post, "/actions", token, body);

    private static async Task<HttpResponseMessage> SendAsync<T>(ApiFactory<T> factory, HttpMethod method, string path, string? token, string? body) where T : class
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private sealed class ApiFactory<T>(TestWorld world, Guid? tenant = null) : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Auth:Mode", "Development");
            builder.UseSetting("Auth:DevSigningKey", Key);
            builder.UseSetting("Auth:AllowedTenants:0", (tenant ?? world.Tenant).ToString());
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Adapters:Mode", "Simulation");
            builder.ConfigureTestServices(services =>
            {
                // Both hosts share one authoritative store and one simulated world.
                services.AddSingleton<IWalletStore>(world.Store);
                services.AddSingleton(world.Environment);
                services.AddSingleton<TimeProvider>(world.Time);
            });
        }
    }
}
