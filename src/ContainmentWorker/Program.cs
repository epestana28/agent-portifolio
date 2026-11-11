using AgentPortifolio.Core.Ports;
using AgentPortifolio.Core.Services;
using AgentPortifolio.Hosting;

// External containment. It reads the outbox straight from the database: a
// message bus can speed delivery up, but the block never depends on one.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWalletStore(builder.Configuration);
builder.Services.AddExternalAdapters(builder.Configuration);
builder.Services.AddSingleton(new ContainmentOptions());
builder.Services.AddSingleton(sp => new ContainmentOrchestrator(sp.GetRequiredService<IWalletStore>(),
    sp.GetServices<IContainmentAdapter>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ContainmentOptions>()));
builder.Services.AddHostedService<OutboxPump>();

var app = builder.Build();
await app.InitialiseAsync();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.Run();

internal sealed class OutboxPump(ContainmentOrchestrator orchestrator, IConfiguration configuration, ILogger<OutboxPump> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Worker:PollSeconds", 2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await orchestrator.ProcessPendingAsync(stoppingToken);
                if (handled > 0)
                {
                    logger.LogInformation("Processed {Count} containment message(s).", handled);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The message stays pending and is retried; the local block is unaffected.
                logger.LogWarning(ex, "Containment pass failed; will retry.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
