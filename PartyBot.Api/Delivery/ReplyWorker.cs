using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartyBot.Api.Application;

namespace PartyBot.Api.Delivery;

public sealed class ReplyWorker(IServiceScopeFactory scopes, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CandidateReadiness>().ProcessAsync(stoppingToken);
            var textSent = await scope.ServiceProvider.GetRequiredService<ReplyDelivery>().DeliverNextAsync(stoppingToken);
            // Use a fresh DbContext after readiness/text delivery so concurrent command changes cannot be read from a stale tracker.
            await using var mediaScope = scopes.CreateAsyncScope();
            var mediaSent = await mediaScope.ServiceProvider.GetRequiredService<PhotoDelivery>().DeliverNextAsync(stoppingToken);
            if (!textSent && !mediaSent)
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
        }
    }
}