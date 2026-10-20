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

## 3. Полный путь фотоответа — DONE (2026-10-19)

Кандидат/альбом, замена, подтверждение, Pending, N2. Персистентные исходящие действия, архив, рабочая карточка, Approve/Reject, ScoreEntry и возвраты. Повторная доставка, роли, конкурентные решения. Получить вертикальный работающий сценарий до баллов. Тесты с fake Telegram и реальной SQLite.

Выполнено:

- Персистентные кандидаты, сборка фотоальбома с продлением debounce, версия, подтверждение/замена, защита от чужих и устаревших callbacks и поздних частей прежнего альбома. Смешанные альбомы отклоняются; Skip инвалидирует кандидата.
- Атомарный snapshot ответа, Pending, освобождение Active и N2. Следующая выдача не зависит от решения модератора.
- Approve/Reject с выбором причины, ID-авторизация организатора и проверка рабочего чата, Open/Review, первое решение побеждает. ScoreEntry и возврат задания фиксируются с решением; повторное начисление защищено уникальным индексом.
- Миграция PhotoAnswers, архив с сохранением альбома и подписей, рабочие карточки, durable outbox, зависимости полноты архива, partial-copy fallback, retry_after, cleanup и итог без кнопок при отказе удаления. Ошибки видны суперадмину и не отменяют решение.
- Fake Telegram расширен для фото, копирования, редактирования и удаления; реальные сетевые вызовы в тестах отсутствуют.

Проверки из корня репозитория:

- `dotnet test PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 72/72, пропусков нет. Реальная SQLite: debounce/рестарт, замена, поздние/смешанные альбомы, роли и старые кнопки, N2, конкурирующие решения, однократные баллы, возвраты, откат транзакции, миграция этапа 2, частичный архив/рабочая копия, retry_after, ошибки cleanup и отсутствие транзакции во время сети.
- `dotnet build PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 0 ошибок и предупреждений.
- `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — модель соответствует миграциям.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --Bot:DatabasePath=/tmp/partybot-stage3-smoke-a91e782c.db --Bot:PollingEnabled=false` — новая SQLite, три миграции, Registration, 3 demo-задания, 0 участников. Host запущен и остановлен Ctrl+C с exit 0. SQLite foreign_key_check пуст.
- `git diff --check` — успешно.

Ограничения: ручная Telegram-репетиция и Docker build не выполнялись; сообщения реальным пользователям не отправлялись. Доставка Telegram не exactly-once. Постоянные ошибки сохраняются в БД; управляемое восстановление и диагностика — этап 6. Рейтинг, панели и проекция бизнес-журнала — следующий этап 4; закрытие и финал — этап 5.

## 4. Рейтинг, панели и аудит — DONE (2026-10-19)

Рейтинг и tie-break, личное место, суперадмин-панель, DQ/Reinstate, отдельная проекция бизнес-журнала. Проверить запреты доступа и сохранение истории. Важные события уже должны записываться на этапах 2–3; здесь завершается удобное отображение.

Выполнено:

- `/rank` и кнопки рейтинга: топ-10 и собственное место по сумме Approved snapshots, max(SubmittedAt), стабильному ID. Pending/Rejected/Unreviewed исключены, нулевые результаты устойчивы, DQ без места.
- Личная `/admin` суперадмина: состояние игры, страницы участников по 10, выбор по имени/номеру, открытие Registration-игры через существующее подтверждение.
- `/dq НОМЕР ПРИЧИНА` и `/reinstate НОМЕР ПРИЧИНА` из карточки, затем Да/Нет. Persisted DisciplineRequest проверяет actor/chat, версии и однократность. Очередь, Active, кандидат, история, баллы и кулдаун сохраняются. Finished запрещает изменения.
- Отдельная проекция AuditEvent в AuditChatId/AuditThreadId, replay существующей истории, уникальные logical keys, retry/постоянные ошибки и уведомления суперадмину. Бизнес-события не изменяются; сбой Telegram не откатывает бизнес-операцию. В события начисления добавлена сумма награды.
- Миграция RankingPanelsAudit добавляет подтверждения дисциплинарных действий; старые данные сохраняются.

Проверки из корня репозитория:

- `dotnet test PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 89/89, без пропусков и предупреждений. Добавлены рейтинг/tie-break/top-10/нулевые результаты, DQ и восстановление с Pending-модерацией, запреты ролей/чатов/Finished, повторные и устаревшие подтверждения, отмена, откат транзакции DQ, сохранение Active/кандидата, пагинация и маршрутизация панели, проекция истории/темы/retry/ошибка журнала, миграция этапа 3.
- `dotnet build PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 0 предупреждений, 0 ошибок.
- `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — модель соответствует миграциям.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --migrate-only --Bot:DatabasePath=/tmp/partybot-stage4-smoke-76ba9b41.db --Bot:PollingEnabled=false` — четыре миграции новой SQLite, exit 0.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --Bot:DatabasePath=/tmp/partybot-stage4-smoke-76ba9b41.db --Bot:PollingEnabled=false` — host запущен, Registration и 3 demo-задания; остановлен Ctrl+C. SQLite foreign_key_check пуст.
- `git diff --check` — успешно.

Ограничения: Telegram API не вызывался, ручная репетиция и Docker build не выполнялись. Создание/чтение итогового snapshot и штатные переходы Review/Finished остаются этапу 5; при внешнем Finished живой рейтинг не подменяет отсутствующий снимок. Управляемое восстановление постоянных ошибок — этап 6. Новые технические решения и команды описаны в DECISIONS/README.

## 5. Подсчёт и финал — DONE (2026-10-20)

CloseIntake, Review, обычный Finalize, ForceFinalize с предупреждением, Unreviewed и snapshot. Гонки с Confirm/Approve, старые кнопки после закрытия. После Finished только чтение.

Выполнено:

- `/close`, `/finalize`, `/forcefinalize ПРИЧИНА` и кнопки панели суперадмина. Персистентные подтверждения/отмена, проверка actor/личного чата/версии, только переходы вперёд. Close фиксирует ClosedAt, сохраняет Active/кандидатов и закрывает все игровые изменения участника; Review допускает модерацию.
- Обычный финал запрещён при Pending. Форс-финал показывает число и последствия, требует причины и отдельного подтверждения; изменение числа Pending требует нового подтверждения.
- Unreviewed без баллов/возвратов/Refill и без изменения кулдауна; аудит, уведомление участника, обновление архива и cleanup через outbox. Доставка продолжает работать после Finished.
- Атомарный snapshot: имя/Telegram ID/DQ/место/очки/max SubmittedAt для всех участников, финальные UTC-метаданные, actor, факт/причина форса и число Unreviewed. После Finished рейтинг читает снимок, игровые изменения и модерация запрещены.
- Миграция GameFinalization сохраняет прежнюю БД; PK/уникальные индексы защищают строки и места снимка от дублирования.

Проверки из корня репозитория:

- `dotnet test PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 106/106, без пропусков и предупреждений. Новые проверки: закрытие всех путей приёма при сохранении Active/кандидата, Review-модерация, запрет обычного финала при Pending, Unreviewed без возвратов/очков, последующая доставка архива, обе очередности гонок Confirm/Close и Approve/ForceFinalize, роли/чаты/отмена/изменившееся число Pending/старые кнопки, атомарный откат финала, неизменность snapshot после рестарта и изменения исходных данных, полный порядок/top-10/личное место/DQ/нулевые результаты и пустой финал, маршрутизация, миграция этапа 4.
- `dotnet build PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 0 предупреждений, 0 ошибок.
- `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — модель соответствует миграциям.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --migrate-only --Bot:DatabasePath=/tmp/partybot-stage5-smoke-5ac218dd.db --Bot:PollingEnabled=false` — пять миграций новой SQLite, exit 0.
- `dotnet run --project PartyBot.Api --launch-profile Bootstrap --no-build -- --Bot:DatabasePath=/tmp/partybot-stage5-smoke-5ac218dd.db --Bot:PollingEnabled=false` — host запущен на той же БД, Registration, 3 demo-задания, пустые FinalStandings; остановлен Ctrl+C. SQLite foreign_key_check пуст.
- `git diff --check` — успешно.

Ограничения: реальный Telegram API и сообщения участникам не использовались; ручная репетиция и Docker build не выполнялись. Диагностика/управляемое восстановление постоянных ошибок доставки и эксплуатационные процедуры остаются этапу 6, ручная репетиция — этапу 7. Команды и технические решения обновлены в README/DECISIONS.

## 6. Восстановление и эксплуатация — DONE (2026-10-20)

Проверить offset/inbox, рестарт на каждом важном шаге, pending deliveries, retry_after, неполный альбом, отказ прав чатов, cleanup. Добавить безопасную диагностику суперадмину: количества Pending, недоставленных архивов, ошибок доски, состояние игры. Обновить OPERATIONS реальными командами, deployment-конфигом и процедурой бэкапа.

Выполнено:

- `/diagnostics` в личной панели суперадмина: game state, Pending, неполные архивы, сборка альбомов, незавершённые/Failed, ошибки доски/cleanup и offset; страницы безопасных ошибок без payload/секретов.
- `/retry НОМЕР` с сохраняемым подтверждением/отменой и проверками ролей/актуальности. Только Failed; известные refs и прогресс cleanup сохраняются, бизнес-решение/баллы/финал не повторяются. Миграция DeliveryRecovery.
- Исправлены повторный отказ медиа после ручного retry (уникальность alert) и восстановление WorkCard после failed WorkMedia. Текстовый сетевой шаг ограничен 15 секундами.
- SQLite-aware backup/verify/restore в новый файл, integrity/FK check, права 0600, тест online WAL. Compose-конфигурация, secret env example, переработанные OPERATIONS и README с процедурами запуска/остановки/резервирования/восстановления.

Проверки из корня репозитория:

- `dotnet test PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 120/120, без пропусков/предупреждений. Включает новую миграцию, повторный старт host с offset/дедупликацией, recovery архива/доски/cleanup/текста, повторный отказ, сохранение финала/очков, частичное удаление с продолжением через новые DbContext, роли/чаты/отмену/старые запросы/откат retry и запрет обхода retry_after. Ранее добавленные проверки неполных альбомов/доставок и рестарта также прошли.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v` — 3/3: backup открытой WAL-БД, восстановление, запрет перезаписи, повреждённый/отсутствующий источник и нарушение FK.
- `dotnet build PartyBot.slnx --no-restore -p:UseSharedCompilation=false` — 0 предупреждений/ошибок; `dotnet ef migrations has-pending-model-changes --project PartyBot.Api --no-build` — изменений модели нет.
- `dotnet publish PartyBot.Api/PartyBot.Api.csproj -c Release --no-restore -p:UseSharedCompilation=false -o /tmp/partybot-stage6-publish-b93e62` — успешно.
- `dotnet /tmp/partybot-stage6-publish-b93e62/PartyBot.Api.dll --environment Development --Bot:DatabasePath=/tmp/partybot-stage6-smoke-b93e62.db --Bot:PollingEnabled=false` — Release host стартовал без Telegram; новая БД с шестью миграциями.
- `python3 scripts/sqlite_backup.py backup /tmp/partybot-stage6-smoke-b93e62.db /tmp/partybot-stage6-backup-b93e62.db`, затем `verify /tmp/partybot-stage6-backup-b93e62.db` и `restore /tmp/partybot-stage6-backup-b93e62.db /tmp/partybot-stage6-restored-b93e62.db` — успешно, integrity/FK OK. Backup сделан при работающем тестовом host.
- После остановки исходного host та же Release-команда с `--Bot:DatabasePath=/tmp/partybot-stage6-restored-b93e62.db` успешно запустила восстановленную копию без polling; оба процесса остановлены Ctrl+C.
- `docker compose -f deploy/compose.yaml config --no-env-resolution --no-interpolate -q` — успешно, без чтения секретов. `docker version --format '{{.Server.Version}}'` — daemon недоступен; Docker build/run не проверены.
- `git diff --check` — успешно.

Ограничения: реальные Telegram-сообщения не отправлялись. Сетевые дубли между send и сохранением ID возможны; восстановление старого backup не гарантирует возврат уже подтверждённых updates. Полная проверка прав/контейнера и репетиция с аккаунтами остаются эксплуатационной проверкой этапа 7.

## 7. Репетиция — TODO

Тестовый бот, 3–5 аккаунтов, отдельные чаты. Полный прогон: регистрация, очередь, альбом, замена, Skip, Reject, двойной Approve, DQ, рестарт, CloseIntake, форс-финал. Затем демонстрационные данные не переносить в production. Зафиксировать результаты и найденные проблемы.
