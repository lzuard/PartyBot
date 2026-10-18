using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartyBot.Api.Configuration;
using PartyBot.Api.Infrastructure;
using PartyBot.Api.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PartyBot.Tests;

public sealed class PollingTests
{
    [Fact]
    public async Task Next_poll_acknowledges_only_after_participant_audit_and_reply_are_committed()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        var updates = new ScriptedUpdates();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development, DisableDefaults = true
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Bot:DatabasePath"] = fixture.DatabasePath });
        builder.Services.AddPartyBot(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
        builder.Services.AddSingleton<ITelegramUpdateSource>(updates);
        builder.Services.AddHostedService<PollingWorker>();
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            Assert.Equal(501, await updates.SecondOffset.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            await using var db = fixture.Open();
            Assert.Single(await db.Participants.ToListAsync());
            Assert.Single(await db.ProcessedUpdates.ToListAsync());
            Assert.Single(await db.AuditEvents.Where(item => item.Action == "Registered").ToListAsync());
            Assert.Single(await db.OutgoingActions.ToListAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Database_lease_prevents_a_second_instance_and_is_released_on_dispose()
    {
        await using var fixture = new GameFixture();
        using (var first = new InstanceLease(fixture.DatabasePath))
        {
            Assert.Throws<InvalidOperationException>(() => new InstanceLease(fixture.DatabasePath));
        }

        using var next = new InstanceLease(fixture.DatabasePath);
    }

    private sealed class ScriptedUpdates : ITelegramUpdateSource
    {
        private int calls;

        public TaskCompletionSource<int?> SecondOffset { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Update[]> FetchAsync(int? offset, CancellationToken cancellationToken)
        {
            if (calls++ == 0)
            {
                Assert.Null(offset);
                return
                [
                    new Update
                    {
                        Id = 500,
                        Message = new Message
                        {
                            Id = 1, Text = "/name Монстр", From = new User { Id = 100, FirstName = "Test" },
                            Chat = new Chat { Id = 100, Type = ChatType.Private }
                        }
                    }
                ];
            }

            SecondOffset.TrySetResult(offset);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public Task AcknowledgeAsync(string callbackId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}