using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Infrastructure;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

public sealed class GameCommands(
    BotDbContext db,
    CommandGate gate,
    TimeProvider time,
    IRandomSource random,
    IOptions<BotOptions> options,
    IHostEnvironment environment)
{
    public async Task<BotReply?> ExecuteAsync(BotCommand command, CancellationToken cancellationToken = default)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (await db.ProcessedUpdates.AnyAsync(update => update.Id == command.UpdateId, cancellationToken))
                return null;
            var game = await db.Games.SingleAsync(cancellationToken);
            BotReply? reply;
            if (PhotoCommands.IsModeration(command.Kind))
                reply = await new PhotoCommands(db, time, options.Value).ModerateAsync(command, game, cancellationToken);
            else
                reply = command.Kind == CommandKind.Ignore || !command.IsPrivate || command.ActorId <= 0 || command.ChatId != command.ActorId
                    ? null : await ApplyAsync(command, game, cancellationToken);
            var now = time.GetUtcNow();
            db.ProcessedUpdates.Add(new ProcessedUpdate { Id = command.UpdateId, ProcessedAt = now });
            (await db.PollingStates.SingleAsync(cancellationToken)).NextUpdateId = checked(command.UpdateId + 1);
            if (reply is not null)
                db.OutgoingActions.Add(new OutgoingAction
                {
                    LogicalKey = $"update:{command.UpdateId}:reply", ChatId = command.ChatId,
                    Payload = JsonSerializer.Serialize(reply), RetryAt = now
                });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reply;
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    private async Task<BotReply?> ApplyAsync(BotCommand command, Game game, CancellationToken cancellationToken)
    {
        if (command.Kind == CommandKind.Leaderboard)
            return await new Leaderboard(db).ReplyAsync(game, command.ActorId, cancellationToken);
        if (AdminCommands.Handles(command.Kind))
            return await new AdminCommands(db, time, options.Value).ApplyAsync(command, game, cancellationToken);
        if (command.Kind is CommandKind.AskOpen or CommandKind.ConfirmOpen or CommandKind.CancelOpen)
            return await OpenAsync(command, game, cancellationToken);
        var participant = await db.Participants.Include(person => person.Tasks).ThenInclude(task => task.Task)
            .Include(person => person.Tasks).ThenInclude(task => task.CurrentAttempt)
            .SingleOrDefaultAsync(person => person.TelegramUserId == command.ActorId, cancellationToken);
        if (participant is not null && participant.PrivateChatId != command.ChatId)
            return new BotReply("Недоступный чат.");
        if (command.Kind == CommandKind.Start)
        {
            if (participant is not null)
                return Menu(participant, game, $"С возвращением, {participant.DisplayName} #{participant.Id}!");
            return game.State is GameState.Registration or GameState.Open
                ? new BotReply("Р-р-р! Представься монстру: /name Ваше имя (1–40 символов).")
                : new BotReply("Регистрация закрыта.");
        }

        if (command.Kind == CommandKind.Name)
        {
            if (game.State is not (GameState.Registration or GameState.Open))
                return new BotReply("Изменения участника закрыты.");
            var name = command.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.EnumerateRunes().Count() > 40 || name.Any(char.IsControl))
                return new BotReply("Имя: 1–40 символов без управляющих знаков. Отправь /name Ваше имя.");
            if (participant is null)
            {
                participant = new Participant
                    { TelegramUserId = command.ActorId, PrivateChatId = command.ChatId, DisplayName = name };
                var catalog = await db.TaskDefinitions.OrderBy(task => task.Id).ToListAsync(cancellationToken);
                PersonalQueue.Initialize(participant, catalog, random);
                db.Participants.Add(participant);
                await db.SaveChangesAsync(
                    cancellationToken); // Allocate stable participant number inside the transaction.
                Audit(command, "Registered", participant, data: JsonSerializer.Serialize(new { Name = name }));
            }
            else if (participant.DisplayName != name)
            {
                Audit(command, "Renamed", participant,
                    data: JsonSerializer.Serialize(new { Old = participant.DisplayName, New = name }));
                participant.DisplayName = name;
                participant.Version++;
            }

            return Menu(participant, game, $"Ты в списке, {participant.DisplayName} #{participant.Id}.");
        }

        if (participant is null) return new BotReply("Сначала зарегистрируйся: /start.");
        if (command.Kind == CommandKind.Help)
            return Menu(participant, game, "Команды: /task, /mytask, /skip, /rank, /name Новое имя.");
        if (game.State != GameState.Open) return new BotReply("Игра сейчас закрыта для заданий.");
        if (participant.Disqualified) return new BotReply("Игровые действия недоступны: участник дисквалифицирован.");
        var active = participant.Tasks.SingleOrDefault(task => task.Location == TaskLocation.Active);
        var photos = new PhotoCommands(db, time, options.Value);
        if (command.Kind == CommandKind.UnsupportedMedia)
            return await photos.UnsupportedAsync(command, participant, active, cancellationToken);
        if (command.Kind == CommandKind.Photo)
            return await photos.ReceiveAsync(command, participant, active, cancellationToken);
        if (command.Kind is CommandKind.ConfirmAnswer or CommandKind.ReplaceAnswer)
            return await photos.CandidateAsync(command, participant, active, game, cancellationToken);
        if (command.Kind == CommandKind.GetTask)
        {
            if (active is not null) return ActiveReply(active);
            var now = time.GetUtcNow();
            if (participant.NextTaskAvailableAt is { } available && now < available)
                return new BotReply(
                    $"До следующего задания: {Math.Ceiling((available - now).TotalSeconds):0} сек. Доступно в {available.ToOffset(TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone).GetUtcOffset(available)):HH:mm:ss zzz}.",
                    GetButtons());
            var attempt = PersonalQueue.Take(participant, now);
            if (attempt is null)
                return new BotReply(participant.Tasks.Any(task => task.Location == TaskLocation.Pending)
                    ? "Доступных заданий нет. Есть ответы, ожидающие модерации."
                    : "Доступных заданий пока нет.", GetButtons());
            db.Attempts.Add(attempt);
            participant.Version++;
            Audit(command, "TaskTaken", participant, attempt);
            return ActiveReply(participant.Tasks.Single(task => task.Location == TaskLocation.Active));
        }

        if (command.Kind == CommandKind.ShowTask)
            return active is null ? new BotReply("Активного задания нет.", GetButtons()) : ActiveReply(active);
        if (active is null || (command.AttemptId is { } expected && expected != active.CurrentAttemptId))
            return new BotReply("Эта попытка уже неактуальна.", MenuButtons(participant));
        if (command.Kind == CommandKind.AskSkip)
            return new BotReply("Пропустить задание?",
            [
                [
                    new BotButton("Да", $"skipyes:{active.CurrentAttemptId:N}"),
                    new BotButton("Нет", $"skipno:{active.CurrentAttemptId:N}")
                ]
            ]);
        if (command.Kind == CommandKind.CancelSkip) return ActiveReply(active);
        if (command.Kind == CommandKind.ConfirmSkip && command.AttemptId is { } attemptId)
        {
            var attempt = active.CurrentAttempt!;
            var now = time.GetUtcNow();
            if (!PersonalQueue.Skip(participant, attemptId, now)) return new BotReply("Эта попытка уже неактуальна.");
            await photos.InvalidateAsync(attemptId, CandidateState.Cancelled, cancellationToken);
            participant.NextTaskAvailableAt = now.AddMinutes(game.SkipCooldownMinutes!.Value);
            participant.Version++;
            Audit(command, "TaskSkipped", participant, attempt);
            Audit(command, "TaskReturnedToQueue", participant, attempt);
            return new BotReply("Задание возвращено в хвост очереди. Следующее — после ожидания.", GetButtons());
        }

        return new BotReply("Неизвестная или устаревшая кнопка.");
    }

    private async Task<BotReply> OpenAsync(BotCommand command, Game game, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (command.ActorId != settings.SuperAdminUserId || command.ChatId != settings.SuperAdminUserId)
            return new BotReply("Открывать игру может только суперадмин в личном чате.");
        if (game.State != GameState.Registration || (command.GameVersion is { } version && version != game.Version))
            return new BotReply("Это подтверждение открытия уже неактуально.");
        if (command.Kind == CommandKind.CancelOpen) return new BotReply("Открытие отменено.");
        var validation = new BotOptionsValidator(environment).ValidateForOpen(settings);
        if (validation.Failed) return new BotReply("Конфигурация игры не готова: " + validation.FailureMessage);
        if (!environment.IsDevelopment() && game.DemonstrationCatalog)
            return new BotReply("Демонстрационный каталог нельзя использовать в production.");
        if (!await db.TaskDefinitions.AnyAsync(cancellationToken)) return new BotReply("Каталог заданий пуст.");
        if (command.Kind == CommandKind.AskOpen)
            return new BotReply("Открыть игру? Каталог и награды будут заморожены.",
                [[new BotButton("Да", $"openyes:{game.Version}"), new BotButton("Нет", $"openno:{game.Version}")]]);
        if (command.GameVersion is null) return new BotReply("Нужно подтверждение актуальной версии игры.");
        game.State = GameState.Open;
        game.Version++;
        game.OpenedAt = time.GetUtcNow();
        game.SkipCooldownMinutes = settings.SkipCooldownMinutes;
        game.SubmitCooldownMinutes = settings.SubmitCooldownMinutes;
        Audit(command, "GameOpened");
        return new BotReply("Игра открыта. Участники могут получать задания.");
    }

    private void Audit(BotCommand command, string action, Participant? participant = null, Attempt? attempt = null,
        string? data = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            At = time.GetUtcNow(), ActorId = command.ActorId, ParticipantId = participant?.Id,
            TaskId = attempt?.TaskId, AttemptId = attempt?.Id, Action = action, Data = data
        });
    }

    private static BotReply Menu(Participant participant, Game game, string text)
    {
        return new BotReply(text + (game.State == GameState.Open ? "" : " Игра сейчас закрыта для заданий."),
            game.State == GameState.Open && !participant.Disqualified ? MenuButtons(participant) : [[new("Рейтинг", "rank")]]);
    }

    private static BotButton[][] GetButtons()
    {
        return [[new BotButton("Получить задание", "get")], [new("Рейтинг", "rank")]];
    }

    private static BotButton[][] MenuButtons(Participant participant)
    {
        return participant.Tasks.SingleOrDefault(task => task.Location == TaskLocation.Active) is { } active
            ? [[new BotButton("Моё задание", "show"), new BotButton("Пропустить", $"skip:{active.CurrentAttemptId:N}")], [new("Рейтинг", "rank")]]
            : GetButtons();
    }

    private static BotReply ActiveReply(TaskAvailability active)
    {
        return new BotReply(
            $"Задание: {active.Task.Text}\nБаллы: {active.Task.Points}\nПришли одну фотографию или один альбом. Фото видят организаторы и сохраняют в архиве для просмотра после вечеринки.",
            [[new BotButton("Моё задание", "show"), new BotButton("Пропустить", $"skip:{active.CurrentAttemptId:N}")], [new("Рейтинг", "rank")]]);
    }
}