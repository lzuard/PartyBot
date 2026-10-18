# Архитектура MVP

## Стек и границы

Default: .NET 10 Worker/Generic Host, Telegram.Bot, long polling, EF Core 10 + SQLite, xUnit. Один процесс, один экземпляр, постоянный локальный диск. PostgreSQL допустим позже, если владелец выберет его; не поддерживать две СУБД заранее. Telegram SDK выбрать совместимой актуальной версии, pin в проекте. SDK API проверять по официальной документации.

Структура после bootstrap:

```text
src/HalloweenBot/
  Telegram/       # polling, маршрутизация, callbacks, альбомы, представления
  Application/    # сценарии игры, транзакции, авторизация
  Domain/         # очереди, состояния, правила рейтинга
  Persistence/    # DbContext, конфигурации EF, миграции
  Delivery/       # персистентные исходящие действия и retries
  Content/        # каталог заданий и реплики
tests/HalloweenBot.Tests/
```

Не обязательны интерфейсы для каждого класса. Достаточно границ Telegram, времени, случайности и хранилища, нужных тестам. Прямое использование DbContext в application допустимо.

## Модель данных

| Сущность | Основные поля и ограничения |
| --- | --- |
| Game | State, OpenedAt, ClosedAt, FinishedAt, N1/N2, Version; одна игра |
| TaskDefinition | StableId, Text, Points, Repeatable; каталог заморожен после Open |
| Participant | Id, TelegramUserId unique, PrivateChatId, DisplayName, DQ, NextTaskAvailableAt, NextBucket, Version |
| TaskAvailability | ParticipantId+TaskId unique, Location Pool/Queue/Active/Pending/Consumed, Position, BucketPoints, CurrentAttemptId |
| Attempt | Id, ParticipantId, TaskId, TakenAt, Submitted/Skipped metadata; история |
| AnswerCandidate | Id, AttemptId, Version, MediaGroupId, LastPartAt, ReadyAt, State |
| CandidateMedia | CandidateId, SourceChatId, SourceMessageId, FileId, FileUniqueId, Order; unique source message |
| Submission | Id, AttemptId unique, ParticipantId, TaskId, TextSnapshot, PointsSnapshot, SubmittedAt, Status, ModeratorId, DecidedAt, RejectReason |
| SubmissionMedia | SubmissionId, Order, FileId, source/archive/work chat+message references, CaptionSnapshot |
| ScoreEntry | Id, SubmissionId unique, ParticipantId, Points, CreatedAt |
| AuditEvent | Stable ID, UTC, Actor, Action, target IDs, reason/data |
| OutgoingAction | Stable logical key unique, payload, Pending/Processing/Done/Failed, attempts, retryAt, refs |
| ProcessedUpdate | UpdateId unique, outcome/processed timestamp |
| FinalStanding | GameId+ParticipantId unique, Rank, Points, LastApprovedSubmittedAt, DQ metadata |

TaskAvailability — предлагаемая реализация инварианта единственного местоположения задания. Допустима другая модель с доказуемыми эквивалентными ограничениями; не дублировать одновременно независимые пул/очередь/status без согласования.

Числовые позиции очереди монотонные; прямой возврат получает новую позицию. Возврат repeatable в пул — в его хвост. Не нужна перенумерация всех строк после каждого GetTask.

FileId нужен для повторной отправки без скачивания; FileUniqueId не годится для отправки. Chat ID хранить вместе с message ID: message ID сам по себе не глобален. Подписи/текст snapshot — в БД. Первичные source message refs сохранять, но не полагаться на их вечную доступность.

## Сценарии

Register, Rename, GetTask, ShowActiveTask, SkipTask, ReplaceCandidate, ConfirmSubmission, ApproveSubmission, RejectSubmission, GetLeaderboard, Disqualify, Reinstate, OpenGame, CloseIntake, FinalizeGame, ForceFinalizeGame.

Telegram handlers валидируют структуру update и вызывают сценарий. Application проверяет роль, состояние игры, принадлежность сущности и версии; эти проверки не ограничивать UI. Для идентификаторов объектов не доверять callback payload без проверки владельца.

## Конкурентность и идемпотентность

Упростить MVP: последовательная обработка входящих бизнес-команд одним потребителем. Альбомы не блокируют consumer ожиданием; сохраняются частями, финализация кандидата планируется отдельно. Outgoing worker выполняет сетевые вызовы независимо. Даже при одном consumer ограничения БД обязательны.

Бизнес-изменение, аудит, задание доставки и отметка обработанного update фиксируются одной транзакцией. Polling offset продвигать только когда update надёжно принят/обработан; не использовать fire-and-forget handlers, теряющие сообщения при рестарте. Если SDK скрывает управление offset, выбрать явный getUpdates loop или персистентный inbox. Обосновать выбранный вариант в реализации.

Approve условно меняет только Pending и создаёт уникальный ScoreEntry. Повторная команда отвечает «уже обработано». Возврат задания — часть той же транзакции. Game state проверяется в транзакции, чтобы финал не гонялся с последним апрувом/ответом.

## Исходящие действия

После ConfirmSubmission: архивировать материалы и карточку; создать рабочую копию и карточку; уведомить участника; отразить событие в журнале. После решения: обновить архивный статус, уведомить участника, удалить рабочую копию при выполненных предпосылках. Статус карточки при поздней доставке строить из актуальной Submission, а не устаревшего payload Pending.

Нужны сохранённые зависимости действий: ArchiveConfirmed → CleanupAllowed. Каждое фото/карточка имеет известный ID и результат доставки. copyMessages может пропустить некопируемые элементы: проверять фактическое число сообщений, не считать частичный результат полным архивом. При необходимости fallback через сохранённые FileId в sendMediaGroup/sendPhoto.

Transient ошибки — retry с backoff и учётом Telegram retry_after. Permanent ошибки (бот заблокирован, нет прав) — обозначить, не ретраить бесконечно без видимости. Нельзя считать весь процесс неуспешным из-за пользовательского уведомления, если бизнес-операция уже сохранена.

Telegram send и запись его ID в БД не атомарны. После сетевого timeout возможен дубль отправки; exactly-once доставки не обещать. Видимый Submission ID позволяет распознать дубль, а callback на любой копии обращается к одной заявке и не удваивает награду. Сомнительная доставка видна в диагностике.

## Рейтинг и аудит

70 участников не требуют кэша. Вычислять суммы и max(SubmittedAt) одобренных ответов; DQ исключать. Finalize атомарно сохраняет окончательные результаты. Бизнес-аудит — append-only. Telegram-журнал — проекция с retry; не источник истины.

## Хранение и время

Все timestamps UTC, сравнение через TimeProvider. SQLite representation/EF ordering выбрать так, чтобы сортировка и сравнения были корректными; проверить integration-тестами. Отображение Europe/Moscow. Сохранять NextTaskAvailableAt, а не Task.Delay на каждого участника.

SQLite хранить вне ephemeral container layer; включить разумный busy timeout, ограничить конкуренцию записей. WAL по необходимости. Резервную копию делать SQLite-aware способом, а не произвольным копированием только .db при активном WAL.
