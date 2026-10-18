using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Delivery;
using PartyBot.Api.Infrastructure;
using PartyBot.Api.Persistence;
using PartyBot.Api.Telegram;
using Telegram.Bot;

namespace PartyBot.Api.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddPartyBot(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton(environment);
        services.AddSingleton<IValidateOptions<BotOptions>, BotOptionsValidator>();
        services.AddOptions<BotOptions>().Bind(configuration.GetSection("Bot")).ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddDbContext<BotDbContext>((provider, options) =>
        {
            var settings = provider.GetRequiredService<IOptions<BotOptions>>().Value;
            var path = Path.GetFullPath(settings.DatabasePath, environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            options.UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path, ForeignKeys = true, DefaultTimeout = 30
            }.ToString());
        });
        services.AddScoped<DatabaseBootstrap>();
        services.AddScoped<CatalogBootstrap>();
        services.AddSingleton<CommandGate>();
        services.AddScoped<GameCommands>();
        if (configuration.GetValue<bool>("Bot:PollingEnabled"))
        {
            services.AddScoped<ReplyDelivery>();
            services.AddSingleton<ITelegramBotClient>(provider =>
                new TelegramBotClient(provider.GetRequiredService<IOptions<BotOptions>>().Value.BotToken));
            services.AddSingleton<TelegramGateway>();
            services.AddSingleton<ITelegramGateway>(provider => provider.GetRequiredService<TelegramGateway>());
            services.AddSingleton<ITelegramUpdateSource>(provider => provider.GetRequiredService<TelegramGateway>());
            services.AddHostedService<PollingWorker>();
            services.AddHostedService<ReplyWorker>();
        }

        return services;
    }
}