using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Tests;

public sealed class GameCommandTests
{
    [Fact]
    public async Task Start_is_idempotent_and_names_have_stable_numbers_without_resetting_progress()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.RunAsync(CommandKind.Start);
        await fixture.RunAsync(CommandKind.Name, name: "  Монстр  ");
        await fixture.RunAsync(CommandKind.Name, 101, "Монстр");
        await fixture.OpenGameAsync();
        await fixture.RunAsync(CommandKind.GetTask);
        var before = await fixture.ParticipantAsync();
        var oldId = before.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId;
        await fixture.RunAsync(CommandKind.Start);
        await fixture.RunAsync(CommandKind.ShowTask);
        await fixture.RunAsync(CommandKind.GetTask);
        await fixture.RunAsync(CommandKind.Name, name: "Другое имя");
        var after = await fixture.ParticipantAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.NextBucket, after.NextBucket);
        Assert.Equal(before.NextPosition, after.NextPosition);
        Assert.Equal(oldId, after.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId);
        Assert.Equal(before.Tasks.OrderBy(t => t.TaskId).Select(t => (t.TaskId, t.Location, t.Position)),
            after.Tasks.OrderBy(t => t.TaskId).Select(t => (t.TaskId, t.Location, t.Position)));
        Assert.NotEqual(after.Id, (await fixture.ParticipantAsync(101)).Id);
        await using var db = fixture.Open();
        Assert.Equal(2, await db.Participants.CountAsync());
        Assert.Single(await db.Attempts.ToListAsync());
        Assert.Single(await db.AuditEvents.Where(e => e.Action == "Renamed").ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    [InlineData("12345678901234567890123456789012345678901")]
    public async Task Invalid_names_do_not_register(string name)
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.RunAsync(CommandKind.Name, name: name);
        await using var db = fixture.Open();
        Assert.Empty(await db.Participants.ToListAsync());
    }

    [Fact]
    public async Task Skip_checks_owner_attempt_and_cooldown_boundary_and_survives_restart()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.OpenGameAsync();
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.RunAsync(CommandKind.Name, 101, "Чужой");
        await fixture.RunAsync(CommandKind.GetTask);
        var before = await fixture.ParticipantAsync();
        var activeId = before.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId!.Value;
        await fixture.RunAsync(CommandKind.ConfirmSkip, 101, attempt: activeId);
        await fixture.RunAsync(CommandKind.AskSkip, attempt: activeId);
        await fixture.RunAsync(CommandKind.CancelSkip, attempt: activeId);
        Assert.Null((await fixture.ParticipantAsync()).NextTaskAvailableAt);
        var skip = fixture.Command(CommandKind.ConfirmSkip, attempt: activeId);
        await fixture.ExecuteAsync(skip);
        await fixture.ExecuteAsync(skip);
        await fixture.RunAsync(CommandKind.ConfirmSkip, attempt: activeId);
        await fixture.RunAsync(CommandKind.Start);
        var skipped = await fixture.ParticipantAsync();
        Assert.Equal(6, PersonalQueue.Queued(skipped).Length);
        Assert.Equal(before.NextBucket, skipped.NextBucket);
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(1), skipped.NextTaskAvailableAt);
        fixture.Clock.Advance(TimeSpan.FromSeconds(59));
        var wait = await fixture.RunAsync(CommandKind.GetTask);
        Assert.Contains("1 сек", wait!.Text);
        Assert.DoesNotContain((await fixture.ParticipantAsync()).Tasks, t => t.Location == TaskLocation.Active);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.RunAsync(CommandKind.GetTask);
        var current = await fixture.ParticipantAsync();
        var newActive = current.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId;
        await fixture.RunAsync(CommandKind.ConfirmSkip, attempt: activeId);
        Assert.Equal(newActive,
            (await fixture.ParticipantAsync()).Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId);
        await using var db = fixture.Open();
        Assert.Single(await db.AuditEvents.Where(e => e.Action == "TaskSkipped").ToListAsync());
        Assert.Single(await db.AuditEvents.Where(e => e.Action == "TaskReturnedToQueue").ToListAsync());
        Assert.Equal(AttemptState.Skipped, (await db.Attempts.FindAsync(activeId))!.State);
    }

    [Fact]
    public async Task Concurrent_duplicate_registration_and_get_produce_one_participant_and_active()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.OpenGameAsync();
        var registration = fixture.Command(CommandKind.Name, name: "Двойник");
        await Task.WhenAll(fixture.ExecuteAsync(registration), fixture.ExecuteAsync(registration));
        await Task.WhenAll(fixture.RunAsync(CommandKind.GetTask), fixture.RunAsync(CommandKind.GetTask));
        await using var db = fixture.Open();
        Assert.Single(await db.Participants.ToListAsync());
        Assert.Single(await db.Attempts.ToListAsync());
        Assert.Equal(3, await db.ProcessedUpdates.CountAsync(update => update.Id != 1));
        Assert.Single(await db.OutgoingActions
            .Where(action => action.LogicalKey == $"update:{registration.UpdateId}:reply").ToListAsync());
    }

    [Fact]
    public async Task Registration_closed_states_dq_and_foreign_chats_block_mutations()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.RunAsync(CommandKind.GetTask);
        Assert.DoesNotContain((await fixture.ParticipantAsync()).Tasks, t => t.Location == TaskLocation.Active);
        await fixture.OpenGameAsync();
        var wrongChat = fixture.Command(CommandKind.GetTask) with { ChatId = 101 };
        await fixture.ExecuteAsync(wrongChat);
        await fixture.ExecuteAsync(fixture.Command(CommandKind.GetTask) with { IsPrivate = false });
        Assert.DoesNotContain((await fixture.ParticipantAsync()).Tasks, t => t.Location == TaskLocation.Active);
        await using (var db = fixture.Open())
        {
            (await db.Participants.SingleAsync()).Disqualified = true;
            await db.SaveChangesAsync();
        }

        await fixture.RunAsync(CommandKind.GetTask);
        Assert.DoesNotContain((await fixture.ParticipantAsync()).Tasks, t => t.Location == TaskLocation.Active);
        foreach (var state in new[] { GameState.Review, GameState.Finished })
        {
            await using (var db = fixture.Open())
            {
                (await db.Games.SingleAsync()).State = state;
                await db.SaveChangesAsync();
            }

            await fixture.RunAsync(CommandKind.Name, 102, "Поздний");
            await fixture.RunAsync(CommandKind.Name, name: "Новое имя");
            await fixture.RunAsync(CommandKind.GetTask);
        }

        await using var final = fixture.Open();
        Assert.Single(await final.Participants.ToListAsync());
        Assert.Equal("Монстр", (await final.Participants.SingleAsync()).DisplayName);
        Assert.Empty(await final.Attempts.ToListAsync());
    }

    [Fact]
    public async Task Open_requires_superadmin_private_chat_configuration_and_current_confirmation()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.RunAsync(CommandKind.ConfirmOpen, 998, version: 0);
        await fixture.ExecuteAsync(fixture.Command(CommandKind.ConfirmOpen, 999, version: 0) with
        {
            ChatId = -1, IsPrivate = false
        });
        await fixture.RunAsync(CommandKind.ConfirmOpen, 999, version: 123);
        await fixture.RunAsync(CommandKind.ConfirmOpen, 999);
        await fixture.RunAsync(CommandKind.AskOpen, 999);
        await fixture.RunAsync(CommandKind.CancelOpen, 999, version: 0);
        await using (var db = fixture.Open())
        {
            Assert.Equal(GameState.Registration, (await db.Games.SingleAsync()).State);
        }

        fixture.Settings.SkipCooldownMinutes = null;
        await fixture.OpenGameAsync();
        await using (var db = fixture.Open())
        {
            Assert.Equal(GameState.Registration, (await db.Games.SingleAsync()).State);
        }

        fixture.Settings.SkipCooldownMinutes = 1;
        await fixture.OpenGameAsync();
        await fixture.OpenGameAsync();
        await using var opened = fixture.Open();
        Assert.Equal(GameState.Open, (await opened.Games.SingleAsync()).State);
        Assert.Single(await opened.AuditEvents.Where(e => e.Action == "GameOpened").ToListAsync());
    }

    [Fact]
    public async Task Sqlite_enforces_single_active_and_unique_task_location()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.OpenGameAsync();
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.RunAsync(CommandKind.GetTask);
        var participant = await fixture.ParticipantAsync();
        await using (var db = fixture.Open())
        {
            db.Attempts.Add(new Attempt
            {
                ParticipantId = participant.Id, TaskId = participant.Tasks[0].TaskId,
                TakenAt = fixture.Clock.GetUtcNow()
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = fixture.Open())
        {
            db.TaskAvailabilities.Add(new TaskAvailability
                { ParticipantId = participant.Id, TaskId = participant.Tasks[0].TaskId });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Reply_record_failure_rolls_back_registration_audit_and_offset()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        var command = fixture.Command(CommandKind.Name, name: "Монстр");
        await using (var db = fixture.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction
                { LogicalKey = $"update:{command.UpdateId}:reply", ChatId = 100, Payload = "{}" });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.ExecuteAsync(command));
        await using var actual = fixture.Open();
        Assert.Empty(await actual.Participants.ToListAsync());
        Assert.Empty(await actual.TaskAvailabilities.ToListAsync());
        Assert.Empty(await actual.AuditEvents.ToListAsync());
        Assert.Empty(await actual.ProcessedUpdates.ToListAsync());
        Assert.Null((await actual.PollingStates.SingleAsync()).NextUpdateId);
    }

    [Fact]
    public async Task Concurrent_skip_and_stale_buttons_after_close_do_not_duplicate_returns()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.OpenGameAsync();
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.RunAsync(CommandKind.GetTask);
        var first = (await fixture.ParticipantAsync()).Tasks.Single(t => t.Location == TaskLocation.Active)
            .CurrentAttemptId;
        await Task.WhenAll(fixture.RunAsync(CommandKind.ConfirmSkip, attempt: first),
            fixture.RunAsync(CommandKind.ConfirmSkip, attempt: first));
        Assert.Equal(6, PersonalQueue.Queued(await fixture.ParticipantAsync()).Length);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.RunAsync(CommandKind.GetTask);
        var before = await fixture.ParticipantAsync();
        var activeId = before.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId;
        await using (var db = fixture.Open())
        {
            (await db.Games.SingleAsync()).State = GameState.Review;
            await db.SaveChangesAsync();
        }

        await fixture.RunAsync(CommandKind.ConfirmSkip, attempt: activeId);
        var after = await fixture.ParticipantAsync();
        Assert.Equal(activeId, after.Tasks.Single(t => t.Location == TaskLocation.Active).CurrentAttemptId);
        Assert.Equal(before.NextTaskAvailableAt, after.NextTaskAvailableAt);
        Assert.Equal(before.NextPosition, after.NextPosition);
    }
}