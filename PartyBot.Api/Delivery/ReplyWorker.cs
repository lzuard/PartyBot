using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PartyBot.Api.Delivery;

public sealed class ReplyWorker(IServiceScopeFactory scopes, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<ReplyDelivery>().DeliverNextAsync(stoppingToken))
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
        }
    }
}