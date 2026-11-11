using System.Security.Claims;
using System.Text;
using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Core.Storage;
using AgentPortifolio.Simulation;
using AgentPortifolio.Storage.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgentPortifolio.Hosting;

public static class Audiences
{
    public const string Control = "api://agent-portifolio-control";
    public const string Executor = "api://agent-portifolio-executor";
}

public static class HostingExtensions
{
    public const string SimulationHeader = "X-AgentPortifolio-Simulation";

    /// <summary>
    /// Token validation. "Entra" validates signature, issuer, audience and lifetime
    /// against the tenant metadata. "Development" uses a local symmetric key and is
    /// only for the local slice. There is no default: a host without an explicit
    /// mode does not start.
    /// </summary>
    public static IServiceCollection AddAgentPortifolioAuth(this IServiceCollection services, IConfiguration configuration, string defaultAudience)
    {
        var mode = configuration["Auth:Mode"] ?? throw new InvalidOperationException("Auth:Mode is required (Entra or Development).");
        var audience = configuration["Auth:Audience"] ?? defaultAudience;
        var allowedTenants = (configuration.GetSection("Auth:AllowedTenants").Get<string[]>() ?? [])
            .Select(t => Guid.TryParse(t, out var id) ? id : throw new InvalidOperationException("Auth:AllowedTenants must contain tenant GUIDs."))
            .ToHashSet();
        if (allowedTenants.Count == 0)
        {
            throw new InvalidOperationException("Auth:AllowedTenants is required. An empty list would accept every tenant.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "oid",
                RoleClaimType = "roles",
            };

            if (string.Equals(mode, "Entra", StringComparison.OrdinalIgnoreCase))
            {
                options.Authority = configuration["Auth:Authority"] ?? throw new InvalidOperationException("Auth:Authority is required in Entra mode.");
            }
            else if (string.Equals(mode, "Development", StringComparison.OrdinalIgnoreCase))
            {
                options.TokenValidationParameters.ValidIssuer = DevTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = DevTokens.Key(
                    configuration["Auth:DevSigningKey"] ?? throw new InvalidOperationException("Auth:DevSigningKey is required in Development mode."));
            }
            else
            {
                throw new InvalidOperationException("Auth:Mode must be Entra or Development.");
            }

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    // Signature and audience are not enough: the tenant must be one we serve
                    // and the caller must carry an object id we can resolve.
                    if (!context.Principal!.TryGetIdentity(out var tenantId, out _) || !allowedTenants.Contains(tenantId))
                    {
                        context.Fail("Tenant or identity not accepted.");
                    }

                    return Task.CompletedTask;
                },
            };
        });
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddWalletStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        var provider = configuration["Storage:Provider"] ?? throw new InvalidOperationException("Storage:Provider is required (SqlServer or InMemory).");
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration["Storage:ConnectionString"] ?? throw new InvalidOperationException("Storage:ConnectionString is required.");
            services.AddSingleton<IWalletStore>(new SqlWalletStore(connectionString));
        }
        else if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IWalletStore>(sp => new InMemoryWalletStore(sp.GetRequiredService<TimeProvider>()));
        }
        else
        {
            throw new InvalidOperationException("Storage:Provider must be SqlServer or InMemory.");
        }

        return services;
    }

    /// <summary>
    /// Registers the simulated Microsoft side. No real Entra or Graph adapter ships
    /// yet (phases C and D), so any other mode stops the host instead of pretending.
    /// </summary>
    public static IServiceCollection AddExternalAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["Adapters:Mode"] ?? throw new InvalidOperationException("Adapters:Mode is required. The only mode available today is Simulation.");
        if (!string.Equals(mode, "Simulation", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Adapters:Mode '{mode}' is not available. Real Microsoft adapters are blocked until the phase A gates are approved; only Simulation exists.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp =>
        {
            var environment = new SimulatedMicrosoftEnvironment();
            var seed = configuration.GetSection("Simulation").Get<SimulationSeed>() ?? new SimulationSeed();
            foreach (var library in seed.Libraries)
            {
                if (!ResourceCanonicalizer.TryLibrary(library.SiteId, library.DriveId, out var resource))
                {
                    throw new InvalidOperationException("Simulation seed contains a library that is not in canonical form.");
                }

                foreach (var document in library.Documents)
                {
                    environment.AddDocument(resource, document.ItemId, document.Name, document.Text);
                }

                foreach (var executor in library.GrantedExecutors)
                {
                    environment.AddIdentity(executor);
                    environment.AddGrant(executor, "Sites.Selected", resource.Canonical, "read");
                }
            }

            return environment;
        });
        services.TryAddSingleton<IDocumentGateway, SimulatedDocumentGateway>();
        services.TryAddSingleton<IExternalGrantSource, SimulatedGrantSource>();
        services.AddSingleton<IContainmentAdapter, SimulatedIdentityDisableAdapter>();
        services.AddSingleton<IContainmentAdapter, SimulatedGrantRemovalAdapter>();
        services.AddSingleton<IContainmentAdapter, SimulatedRuntimeIsolationAdapter>();
        services.AddSingleton<IContainmentAdapter, SimulatedIssuedTokenProbeAdapter>();
        return services;
    }

    public static async Task InitialiseAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AgentPortifolio");
        logger.LogWarning("External adapters run in SIMULATION mode. Nothing this host reports about Entra, Graph or SharePoint is real evidence.");
        if (string.Equals(app.Configuration["Auth:Mode"], "Development", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Auth runs in DEVELOPMENT mode with a local signing key. Do not expose this host.");
        }

        if (app.Services.GetRequiredService<IWalletStore>() is SqlWalletStore sql && app.Configuration.GetValue("Storage:EnsureSchema", true))
        {
            // The database container may still be starting.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (app.Configuration.GetValue("Storage:CreateDatabase", false))
                    {
                        await sql.EnsureDatabaseAsync(app.Lifetime.ApplicationStopping);
                    }

                    await sql.EnsureSchemaAsync(app.Lifetime.ApplicationStopping);
                    break;
                }
                catch (Exception ex) when (attempt < 30 && ex is not OperationCanceledException)
                {
                    logger.LogInformation("Database not ready (attempt {Attempt}): {Reason}", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(2), app.Lifetime.ApplicationStopping);
                }
            }
        }
    }

    /// <summary>Maps domain failures to HTTP without leaking internals.</summary>
    public static void UseDomainErrors(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.Response.HasStarted && Map(ex) is var (status, code) && status != 0)
            {
                context.Response.StatusCode = status;
                await context.Response.WriteAsJsonAsync(new { error = code, detail = ex is StoreUnavailableException ? null : ex.Message });
            }
        });

    public static bool TryGetIdentity(this ClaimsPrincipal principal, out Guid tenantId, out Guid objectId)
    {
        objectId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue("tid"), out tenantId) && Guid.TryParse(principal.FindFirstValue("oid"), out objectId);
    }

    public static Operator ToOperator(this ClaimsPrincipal principal) =>
        principal.TryGetIdentity(out var tenantId, out var objectId)
            ? new Operator(tenantId, objectId, principal.FindAll("roles").Select(c => c.Value).ToHashSet(StringComparer.Ordinal))
            : throw new ForbiddenException("Token carries no usable identity.");

    public static CallerIdentity ToCaller(this ClaimsPrincipal principal) =>
        principal.TryGetIdentity(out var tenantId, out var objectId)
            ? new CallerIdentity(tenantId, objectId)
            : throw new ForbiddenException("Token carries no usable identity.");

    private static (int Status, string Code) Map(Exception ex) => ex switch
    {
        ForbiddenException => (StatusCodes.Status403Forbidden, "forbidden"),
        NotFoundException => (StatusCodes.Status404NotFound, "not_found"),
        RequestValidationException => (StatusCodes.Status400BadRequest, "invalid_request"),
        InvalidTransitionException t => (StatusCodes.Status409Conflict, t.ReasonCode),
        ConcurrencyConflictException => (StatusCodes.Status409Conflict, "conflict"),
        StoreUnavailableException => (StatusCodes.Status503ServiceUnavailable, ReasonCodes.StateUnavailable),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request"),
        _ => (0, string.Empty),
    };
}

public sealed class SimulationSeed
{
    public List<SeedLibrary> Libraries { get; set; } = [];
}

public sealed class SeedLibrary
{
    public string SiteId { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;
    public List<Guid> GrantedExecutors { get; set; } = [];
    public List<SeedDocument> Documents { get; set; } = [];
}

public sealed class SeedDocument
{
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>DEVELOPMENT ONLY. Mints tokens for the local slice. Entra is the issuer everywhere else.</summary>
public static class DevTokens
{
    public const string Issuer = "https://dev-issuer.agent-portifolio.invalid";

    public static SymmetricSecurityKey Key(string secret) =>
        Encoding.UTF8.GetByteCount(secret) >= 32
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
            : throw new InvalidOperationException("Auth:DevSigningKey must be at least 32 bytes.");

    public static string Create(string secret, string audience, Guid tenantId, Guid objectId, IEnumerable<string> roles, TimeSpan lifetime, DateTime? now = null)
    {
        var issuedAt = now ?? DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["tid"] = tenantId.ToString(), ["oid"] = objectId.ToString() };
        var roleList = roles.ToArray();
        if (roleList.Length > 0)
        {
            claims["roles"] = roleList;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + lifetime,
            SigningCredentials = new SigningCredentials(Key(secret), SecurityAlgorithms.HmacSha256),
        });
    }
}
