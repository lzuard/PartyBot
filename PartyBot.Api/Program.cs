using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PartyBot.Api.Configuration;
using PartyBot.Api.Persistence;
using PartyBot.Api.Application;
using PartyBot.Api.Infrastructure;
using Microsoft.Extensions.Options;

var migrateOnly = args.Contains("--migrate-only");
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(arg => arg != "--migrate-only").ToArray(),
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddPartyBot(builder.Configuration, builder.Environment);
using var host = builder.Build();
var settings = host.Services.GetRequiredService<IOptions<BotOptions>>().Value;
using var lease = new InstanceLease(Path.GetFullPath(settings.DatabasePath, builder.Environment.ContentRootPath));
await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseBootstrap>().InitializeAsync();
    if (!migrateOnly) await scope.ServiceProvider.GetRequiredService<CatalogBootstrap>().InitializeAsync();
}
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap")
    .LogInformation("SQLite ready. Telegram polling enabled: {PollingEnabled}.", !migrateOnly && settings.PollingEnabled);
if (!migrateOnly)
    await host.RunAsync();
