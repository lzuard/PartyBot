# План реализации

Статусы обновляются по фактически выполненным этапам. По окончании этапа отметить DONE, указать реальные команды и результаты. Не помечать ручную репетицию выполненной без неё.

## 1. Bootstrap и конфигурация — DONE (2026-10-18)

Создать solution, основной проект и xUnit. Настроить конфигурацию/секреты/логирование, DbContext/миграции, тестовый fake Telegram gateway, TimeProvider и random. Пример каталога пометить демонстрационным. Проверки build и миграции чистой SQLite. README дополнить фактическими командами. Не писать весь бот в этом этапе.

Выполнено: существующий PartyBot.Api преобразован в Generic Host .NET 10; добавлены
конфигурация и secret providers, безопасные development placeholders, EF Core 10/SQLite,
начальная миграция инфраструктурной таблицы, TimeProvider/IRandomSource, минимальная
граница Telegram и fake, xUnit. Демонстрационный каталог явно помечен и не импортируется.
Бизнес-сущности и polling оставлены следующим этапам.

Проверки из корня репозитория:

- `dotnet tool restore` — dotnet-ef 10.0.13 восстановлен.
- `dotnet restore PartyBot.slnx` — успешно после обновления зависимостей.
- `dotnet build PartyBot.slnx --no-restore` — успешно, 0 предупреждений, 0 ошибок.
- `dotnet test PartyBot.slnx --no-restore` — 6/6, реальная файловая SQLite; миграция с нуля, повторный bootstrap, сохранение UTC, SQL-сравнение времени, singleton constraint, конфигурация, fake gateway и внедрение тестовых зависимостей.
- `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — изменений относительно snapshot нет (после сборки сгенерированной миграции).
- `dotnet ef migrations list --project PartyBot.Api --no-build` — начальная миграция найдена; без override используется отдельная default БД, поэтому до её запуска статус Pending ожидаем.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --migrate-only --Bot:DatabasePath=/tmp/partybot-stage1-check-20261018.db` — новая БД мигрирована, exit 0.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --Bot:DatabasePath=/tmp/partybot-stage1-check-20261018.db` — host стартовал на той же БД и остановлен Ctrl+C; одна bootstrap-строка сохранена.
- `dotnet list PartyBot.slnx package --vulnerable --include-transitive --no-restore` — уязвимых пакетов по текущим источникам не найдено.
- `git diff --check` — успешно.

Первоначальное восстановление нативного SQLite-пакета зависало в NuGet; официальный
пакет был загружен через HTTP/1.1 во временный источник с проверкой Content-MD5.
После обновления зависимостей обычный restore работает. Временный источник не добавлен
в настройки репозитория. Docker build и ручная Telegram-репетиция не выполнялись.

## 2. Домен, очередь, регистрация — DONE (2026-10-18)

Реализовать состояния, регистрацию, персональные пулы/очередь/Cursor, выдачу, Skip и кулдауны. Сначала все инварианты и примеры QUEUE.md, затем тонкий Telegram вход /start, имя, GetTask/ShowTask/Skip. Покрыть ACCEPTANCE про очередь и повторную регистрацию. В конце уже можно вручную взять и пропустить задание.

Выполнено:

- Game/Participant/TaskDefinition/TaskAvailability/Attempt, миграция GameQueueRegistration; уникальность Telegram ID и местоположения задания, filtered unique indexes для единственного Active, FK и version concurrency tokens.
- Начальные случайные персональные пулы, циклический Cursor, Refill, выдача головы, прямой возврат при Skip, N1 в UTC; примитивы Pending/Approve/Reject алгоритма очереди проверены без реализации модерации.
- `/start`, `/name`, `/task`, `/mytask`, `/skip` и inline-кнопки. Подтверждение Да/Нет, проверка владельца/чата/игры/attempt ID; повторная регистрация и старые callbacks не сбрасывают прогресс.
- Минимальный `/open` суперадмина с подтверждением версии и проверкой конфигурации, чтобы ручной GetTask/Skip был доступен без обхода game state.
- Импорт и валидация демонстрационного каталога при создании игры; сохранённый каталог и очереди не пересобираются при рестарте.
- Аудит регистрации/имени/выдачи/пропуска/возврата/открытия; последовательные транзакционные команды, ProcessedUpdate, durable offset и минимальная очередь текстовых ответов. Полная медиа-доставка остаётся этапу 3.
- Long polling включается только явным `Bot:PollingEnabled=true`; по умолчанию Development работает без токена и сети. Проверена блокировка второго экземпляра на той же БД.

Проверки из корня репозитория:

- `dotnet build PartyBot.slnx --no-restore` — успешно, 0 предупреждений, 0 ошибок.
- `dotnet test PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 42/42, пропусков нет. Включает пример QUEUE.md, пустые бакеты, воспроизводимый seed, очередь >6, возврат repeatable в хвост своего пула, одноразовые задания, конкурентные GetTask/Skip, чужие/старые кнопки, границу N1, закрытые состояния/DQ, повторный /start, сохранение через новые DbContext, миграцию чистой SQLite и обновление этапа 1.
- Проверены атомарный откат регистрации/аудита/offset при ошибке записи исходящего ответа, повторная доставка после сбоя, незаблокированная доставка другим пользователям при 403 и порядок commit → следующий polling offset. Использованы fake Telegram gateway/update source, без реального API.
- `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — модель соответствует миграции.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --Bot:DatabasePath=/tmp/partybot-stage2-smoke-8c64bf12.db --Bot:PollingEnabled=false` — новая БД, обе миграции, Registration и 3 demo-задания; host стартовал и остановлен Ctrl+C с exit 0. SQLite foreign_key_check пуст, участников нет.
- `git diff --check` — успешно.

Ограничения: ручной Telegram-прогон и Docker build не выполнялись; реальные сообщения
не отправлялись. Проверка прав Telegram-поверхностей перед реальной игрой остаётся
операционной проверкой. Фотоответы/кандидаты, модерация, начисления, рейтинг,
полные админ-панели и финал не реализованы. Повторный импорт/редактор каталога
существующей Registration-игры не добавлялся; каталог нужно подготовить до первого
запуска целевой БД. Технические решения и реальные команды описаны в DECISIONS/README.

## 3. Полный путь фотоответа — TODO

Кандидат/альбом, замена, подтверждение, Pending, N2. Персистентные исходящие действия, архив, рабочая карточка, Approve/Reject, ScoreEntry и возвраты. Повторная доставка, роли, конкурентные решения. Получить вертикальный работающий сценарий до баллов. Тесты с fake Telegram и реальной SQLite.

## 4. Рейтинг, панели и аудит — TODO

Рейтинг и tie-break, личное место, суперадмин-панель, DQ/Reinstate, отдельная проекция бизнес-журнала. Проверить запреты доступа и сохранение истории. Важные события уже должны записываться на этапах 2–3; здесь завершается удобное отображение.

## 5. Подсчёт и финал — TODO

CloseIntake, Review, обычный Finalize, ForceFinalize с предупреждением, Unreviewed и snapshot. Гонки с Confirm/Approve, старые кнопки после закрытия. После Finished только чтение.

## 6. Восстановление и эксплуатация — TODO

Проверить offset/inbox, рестарт на каждом важном шаге, pending deliveries, retry_after, неполный альбом, отказ прав чатов, cleanup. Добавить безопасную диагностику суперадмину: количества Pending, недоставленных архивов, ошибок доски, состояние игры. Обновить OPERATIONS реальными командами, deployment-конфигом и процедурой бэкапа.

## 7. Репетиция — TODO

Тестовый бот, 3–5 аккаунтов, отдельные чаты. Полный прогон: регистрация, очередь, альбом, замена, Skip, Reject, двойной Approve, DQ, рестарт, CloseIntake, форс-финал. Затем демонстрационные данные не переносить в production. Зафиксировать результаты и найденные проблемы.
