namespace PartyBot.Api.Configuration;

public sealed class BotOptions
{
    public string BotToken { get; set; } = "";
    public long SuperAdminUserId { get; set; }
    public long[] OrganizerUserIds { get; set; } = [];
    public long ModerationChatId { get; set; }
    public long ArchiveChatId { get; set; }
    public long AuditChatId { get; set; }
    public int? AuditThreadId { get; set; }
    public int? SkipCooldownMinutes { get; set; }
    public int? SubmitCooldownMinutes { get; set; }
    public string DatabasePath { get; set; } = "data/partybot.db";
    public string CatalogPath { get; set; } = "Content/catalog.demo.json";
    public string DisplayTimeZone { get; set; } = "Europe/Moscow";
}
