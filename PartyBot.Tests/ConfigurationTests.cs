using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PartyBot.Api.Configuration;
using PartyBot.Api.Infrastructure;
using PartyBot.Api.Telegram;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

public sealed class ConfigurationTests
{
    private static HostApplicationBuilder CreateBuilder(string environment)
    {
        return Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            ContentRootPath = AppContext.BaseDirectory,
            DisableDefaults = true
        });
    }

    [Fact]
    public void Development_configuration_loads_placeholders_and_supports_test_overrides()
    {
        var builder = CreateBuilder(Environments.Development);
        builder.Configuration.AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json");
        builder.Services.AddPartyBot(builder.Configuration, builder.Environment);
        var clock = new FakeTimeProvider();
        var gateway = new FakeTelegramGateway();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<ITelegramGateway>(gateway);
        builder.Services.AddSingleton<IRandomSource>(new SeededRandomSource(42));
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<BotOptions>>().Value;
        Assert.Equal(1, options.SkipCooldownMinutes);
        Assert.Equal(1, options.SubmitCooldownMinutes);
        Assert.Equal("Europe/Moscow", options.DisplayTimeZone);
        Assert.Same(clock, host.Services.GetRequiredService<TimeProvider>());
        Assert.Same(gateway, host.Services.GetRequiredService<ITelegramGateway>());
        var expected = new Random(42);
        var random = host.Services.GetRequiredService<IRandomSource>();
        Assert.Equal(Enumerable.Range(0, 20).Select(_ => expected.Next(100)).ToArray(),
            Enumerable.Range(0, 20).Select(_ => random.Next(100)).ToArray());
    }

    [Fact]
    public void Production_rejects_placeholders_without_echoing_token()
    {
        var builder = CreateBuilder(Environments.Production);
        var options = new BotOptions { BotToken = "PLACEHOLDER-private-value" };
        var result = new BotOptionsValidator(builder.Environment).Validate(null, options);
        Assert.True(result.Failed);
        Assert.DoesNotContain(options.BotToken, result.FailureMessage);
        Assert.Contains("cooldowns", result.FailureMessage);
    }

    [Fact]
    public void Explicit_production_settings_pass_and_chat_conflict_fails()
    {
        var builder = CreateBuilder(Environments.Production);
        var options = new BotOptions
        {
            BotToken = "synthetic-test-value", SuperAdminUserId = 123,
            OrganizerUserIds = [456], ModerationChatId = -1, ArchiveChatId = -2, AuditChatId = -3,
            DatabasePath = Path.Combine(Path.GetTempPath(), "partybot.db"),
            SkipCooldownMinutes = 0, SubmitCooldownMinutes = 2
        };
        var validator = new BotOptionsValidator(builder.Environment);
        Assert.True(validator.Validate(null, options).Succeeded);
        options.ArchiveChatId = options.ModerationChatId;
        Assert.True(validator.Validate(null, options).Failed);
    }

    [Fact]
    public async Task Fake_gateway_records_delivery_and_can_simulate_failure()
    {
        var gateway = new FakeTelegramGateway { NextFailure = new IOException("Simulated network failure") };
        await Assert.ThrowsAsync<IOException>(() => gateway.SendTextAsync(42, "test"));
        Assert.Empty(gateway.Sent);
        var reference = await gateway.SendTextAsync(42, "test");
        Assert.Equal(42, reference.ChatId);
        Assert.Single(gateway.Sent);
    }

    [Fact]
    public void Offline_host_builds_without_a_telegram_client_or_delivery_workers()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development, ContentRootPath = AppContext.BaseDirectory
        });
        builder.Services.AddPartyBot(builder.Configuration, builder.Environment);
        using var host = builder.Build();
        Assert.False(host.Services.GetRequiredService<IOptions<BotOptions>>().Value.PollingEnabled);
        Assert.Null(host.Services.GetService<ITelegramGateway>());
        Assert.Empty(host.Services.GetServices<IHostedService>());
    }
}