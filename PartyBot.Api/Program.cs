using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PartyBot.Api.Configuration;
using PartyBot.Api.Persistence;

var migrateOnly = args.Contains("--migrate-only");
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(arg => arg != "--migrate-only").ToArray(),
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddPartyBot(builder.Configuration, builder.Environment);
using var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseBootstrap>().InitializeAsync();
}
host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap")
    .LogInformation("SQLite ready. Bootstrap only: Telegram polling and delivery are not started.");
if (!migrateOnly)
    await host.RunAsync();
