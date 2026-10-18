using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PartyBot.Api.Configuration;

namespace PartyBot.Api.Persistence;

// Design-time commands must not run the host or require Telegram secrets.
public sealed class BotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables().AddCommandLine(args).Build();
        var settings = configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions();
        var path = Path.GetFullPath(settings.DatabasePath, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path, ForeignKeys = true, DefaultTimeout = 30
        };
        return new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection.ToString()).Options);
    }
}
