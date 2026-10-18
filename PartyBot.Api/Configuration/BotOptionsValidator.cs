using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PartyBot.Api.Configuration;

public sealed class BotOptionsValidator(IHostEnvironment environment) : IValidateOptions<BotOptions>
{
    public ValidateOptionsResult Validate(string? name, BotOptions options)
        => ValidateCore(options, !environment.IsDevelopment() || options.PollingEnabled);

    public ValidateOptionsResult ValidateForOpen(BotOptions options) => ValidateCore(options, true);

    private static ValidateOptionsResult ValidateCore(BotOptions options, bool requireComplete)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.DatabasePath) || options.DatabasePath == ":memory:")
            errors.Add("Bot:DatabasePath must be a persistent file path.");
        if (string.IsNullOrWhiteSpace(options.CatalogPath))
            errors.Add("Bot:CatalogPath is required.");
        if (options.SkipCooldownMinutes is < 0 || options.SubmitCooldownMinutes is < 0)
            errors.Add("Cooldowns cannot be negative.");
        try { TimeZoneInfo.FindSystemTimeZoneById(options.DisplayTimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { errors.Add("Bot:DisplayTimeZone is invalid."); }
        var chats = new[] { options.ModerationChatId, options.ArchiveChatId, options.AuditChatId };
        if (chats.Where(id => id != 0).Distinct().Count() != chats.Count(id => id != 0))
            errors.Add("Moderation, archive and audit chat IDs must differ.");
        if (requireComplete)
        {
            if (string.IsNullOrWhiteSpace(options.BotToken) || options.BotToken.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
                errors.Add("Bot:BotToken must be provided via secrets/environment.");
            if (options.SuperAdminUserId <= 0 || options.OrganizerUserIds.Any(id => id <= 0))
                errors.Add("Role IDs must be positive Telegram user IDs.");
            if (chats.Any(id => id == 0) || options.AuditThreadId is <= 0)
                errors.Add("Chat IDs must be configured; thread ID, if set, must be positive.");
            if (options.SkipCooldownMinutes is null || options.SubmitCooldownMinutes is null)
                errors.Add("Production cooldowns must be explicitly configured.");
            if (!Path.IsPathFullyQualified(options.DatabasePath))
                errors.Add("Production DatabasePath must be absolute.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
