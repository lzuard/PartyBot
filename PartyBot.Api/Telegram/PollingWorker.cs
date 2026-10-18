using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PartyBot.Api.Application;
using PartyBot.Api.Persistence;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace PartyBot.Api.Telegram;

public sealed class PollingWorker(IServiceScopeFactory scopes, ITelegramUpdateSource source,
    TimeProvider time, ILogger<PollingWorker> logger, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Update[] updates;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
                var offset = await db.PollingStates.Select(state => state.NextUpdateId).SingleAsync(stoppingToken);
                updates = await source.FetchAsync(offset, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (ApiRequestException error) when (error.ErrorCode is 401 or 409)
            {
                logger.LogError("Polling stopped: Telegram error {Code}. Check credentials and single polling instance/webhook.", error.ErrorCode);
                lifetime.StopApplication();
                return;
            }
            catch (Exception error) when (error is ApiRequestException or HttpRequestException or OperationCanceledException)
            {
                logger.LogWarning("Polling request failed ({ErrorType}); retrying without advancing offset.", error.GetType().Name);
                var seconds = error is ApiRequestException api ? api.Parameters?.RetryAfter ?? 5 : 5;
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), time, stoppingToken);
                continue;
            }
            foreach (var update in updates)
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GameCommands>().ExecuteAsync(UpdateRouter.Map(update), stoppingToken);
                // The update, domain changes and reply are durable before the next getUpdates acknowledges it.
                if (update.CallbackQuery is { } callback)
                {
                    try { await source.AcknowledgeAsync(callback.Id, stoppingToken); }
                    catch (Exception error) when (error is ApiRequestException or HttpRequestException ||
                                                  error is OperationCanceledException && !stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Callback acknowledgement unavailable ({ErrorType}); durable reply remains queued.", error.GetType().Name);
                    }
                }
            }
        }
    }
}
