using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PartyBot.Api.Application;
using PartyBot.Api.Persistence;
using PartyBot.Api.Telegram;
using Telegram.Bot.Exceptions;

namespace PartyBot.Api.Delivery;

public sealed class ReplyDelivery(
    BotDbContext db,
    ITelegramGateway gateway,
    TimeProvider time,
    ILogger<ReplyDelivery> logger)
{
    // Called by one delivery consumer. No transaction spans the Telegram request.
    public async Task<bool> DeliverNextAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var action = await db.OutgoingActions
            .Where(item => item.Kind == DeliveryKind.Text && !item.Failed && item.MessageId == null && item.RetryAt <= now)
            .OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        if (action is null) return false;
        try
        {
            var reply = JsonSerializer.Deserialize<BotReply>(action.Payload) ??
                        throw new InvalidOperationException("Invalid reply payload.");
            var sent = await gateway.SendReplyAsync(action.ChatId, reply, cancellationToken);
            action.MessageId = sent.MessageId;
        }
        catch (Exception error) when (error is ApiRequestException or HttpRequestException or IOException ||
                                      (error is OperationCanceledException &&
                                       !cancellationToken.IsCancellationRequested))
        {
            action.Attempts++;
            action.Failed = error is ApiRequestException { ErrorCode: 400 or 401 or 403 };
            var delay = error is ApiRequestException { Parameters.RetryAfter: { } retryAfter }
                ? retryAfter
                : Math.Min(300, 2 << Math.Min(action.Attempts, 7));
            action.RetryAt = now.AddSeconds(Math.Max(1, delay));
            logger.LogWarning("Reply {ActionId} failed ({ErrorType}); permanent: {Permanent}.", action.Id,
                error.GetType().Name, action.Failed);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}