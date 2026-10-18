# PartyBot

Telegram-бот одной Halloween-вечеринки. Реализованы этапы 1–2: .NET 10 Generic Host,
SQLite, регистрация, персональные пулы/очередь, выдача заданий, подтверждаемый пропуск
и кулдаун. Фотоответы, модерация, рейтинг и завершение игры — следующие этапы.
Спецификации: [docs/README.md](docs/README.md), прогресс: [docs/TASKS.md](docs/TASKS.md).

## Сборка и безопасный локальный запуск

Нужен .NET SDK 10.0.400 или более новый patch этой feature band. Из корня репозитория:

```sh
dotnet restore PartyBot.slnx
dotnet tool restore
dotnet build PartyBot.slnx --no-restore
dotnet test PartyBot.slnx --no-build
dotnet run --project PartyBot.Api --launch-profile Bootstrap
```

По умолчанию `Bot:PollingEnabled=false`: запросов к Telegram нет, токен не нужен.
Приложение применяет миграции, при первом обычном запуске импортирует демонстрационный
каталог, создаёт игру в Registration и ждёт Ctrl+C. Перезапуск сохраняет данные.
Профиль Bootstrap задаёт `DOTNET_ENVIRONMENT=Development`.

Только миграции, без импорта каталога, polling и ожидания Ctrl+C:

```sh
dotnet run --project PartyBot.Api --launch-profile Bootstrap -- --migrate-only
dotnet ef migrations list --project PartyBot.Api
dotnet ef migrations has-pending-model-changes --project PartyBot.Api
```

Для отдельной БД можно переопределить путь:

```sh
dotnet run --project PartyBot.Api --launch-profile Bootstrap -- --migrate-only --Bot:DatabasePath=/tmp/partybot-stage2-check.db
```

Миграция `GameQueueRegistration` обновляет БД этапа 1 без удаления прежних данных.
При обычном запуске файл `DATABASE.db.lock` предотвращает второй процесс на той же
БД; после остановки файл может оставаться, блокировка освобождается. EF CLI не
использует эту блокировку: миграции вручную выполняйте при остановленном приложении.

## Подключение отдельного тестового Telegram-бота

Это инструкция для ручной репетиции; при разработке реальные сообщения не отправлялись.
Для polling и `/open` заполните:

- `Bot:BotToken` — токен отдельного тестового бота;
- `Bot:SuperAdminUserId` — ваш Telegram user ID; `Bot:OrganizerUserIds` — ID организаторов;
- `Bot:ModerationChatId`, `Bot:ArchiveChatId`, `Bot:AuditChatId` — разные отдельные тестовые чаты;
- `Bot:DatabasePath` — абсолютный путь к отдельной постоянной тестовой БД;
- N1/N2 — в Development доступны явно помеченные defaults 1/1 минута.

Секреты храните в .NET user-secrets проекта или переменных окружения. Например,
ключ `Bot:BotToken` в user-secrets соответствует переменной `Bot__BotToken`.
Не записывайте токен в отслеживаемые JSON-файлы или аргументы запуска.

После настройки включение polling выполняется явно:

```sh
dotnet run --project PartyBot.Api --launch-profile Bootstrap -- --Bot:PollingEnabled=true
```

Не запускайте другой polling с тем же токеном даже на другой БД. Бот не удаляет
webhook и не сбрасывает ожидающие updates. Ошибки Telegram 401/409 останавливают
polling; нужно исправить конфигурацию или конфликт экземпляров.

В личном чате тестового бота:

1. `/start` → `/name Ваше имя`. Совпадающие имена различаются номером `#Id`.
2. Суперадмин отправляет `/open` и нажимает «Да». До подтверждения выдача закрыта.
3. `/task` или «Получить задание» выдаёт голову персональной очереди.
4. `/mytask` показывает текущую попытку без новой выдачи.
5. `/skip` или «Пропустить» → «Да» возвращает задание в хвост и запускает N1;
   «Нет» сохраняет попытку. До границы кулдауна бот показывает оставшиеся секунды.
6. `/name Новое имя` меняет имя с аудитом, сохраняя очередь и историю.

Все callback-команды проверяют actor, личный chat, состояние игры и актуальность
попытки/версии. Старые кнопки не выполняют повторный возврат. Доступность действий
проверяется в application, а не только в клавиатуре. Рейтинг, фото и модерация пока
не доступны. Действия CloseIntake/Finalize появятся в этапе 5; обратного перехода
игры нет. Для новой независимой тестовой игры выбирайте новую БД.

## Конфигурация и каталог

Приоритет: `appsettings.json` → `appsettings.{Environment}.json` → user-secrets
(Development) → environment → аргументы командной строки. Относительные пути
разрешаются от каталога исполняемого приложения; Debug-БД по умолчанию:
`PartyBot.Api/bin/Debug/net10.0/data/partybot.db`.

Остальные настройки:

- `Bot__OrganizerUserIds__0`, `Bot__OrganizerUserIds__1`: элементы списка ID;
- `Bot__AuditThreadId`: необязательная тема журнала;
- `Bot__SkipCooldownMinutes`, `Bot__SubmitCooldownMinutes`: N1/N2, явно задать вне Development;
- `Bot__CatalogPath`: JSON-каталог; default `Content/catalog.demo.json`;
- `Bot__DisplayTimeZone`: Europe/Moscow; все timestamps в БД — INTEGER UTC ticks.

Каталог содержит `demonstrationOnly` и `tasks`: `stableId`, `text`, `points`
(положительное целое, default 1), `repeatable`. ID уникальны. Каталог импортируется
только при создании игры, затем источником служат таблицы БД: редактирование JSON
не меняет существующую игру, награды или персональные пулы. Отдельный редактор/
повторный импорт для существующей Registration-игры в этот этап не входит.
Подготовьте каталог до первого запуска целевой БД. После Open он заморожен.
Демонстрационный каталог допускается только для Development-открытия;
production `/open` его отклоняет.

Вне Development, а также при включении polling и при `/open`, проверяются токен,
ID, разные поверхности, явные N1/N2 и абсолютный путь БД. Это проверка конфигурации,
не Telegram-прав на отправку/редактирование/удаление: права и полную доставку фото
нужно будет проверить перед реальной игрой по [OPERATIONS](docs/OPERATIONS.md).

## Надёжность и тесты

Один основной проект `PartyBot.Api` (историческое имя, HTTP-сервера нет) и один
`PartyBot.Tests`. Domain не зависит от Telegram. TimeProvider и IRandomSource
заменяются в тестах; интеграционные тесты используют файловую SQLite и fake gateway.

Входящие команды сериализуются общим gate. Изменения игры, аудит, ProcessedUpdate,
offset и исходящий текстовый ответ фиксируются одной транзакцией. Явный getUpdates
читает сохранённый offset. Отдельный worker отправляет ответы без транзакции БД
на время запроса, повторяет временные ошибки, учитывает retry_after и отмечает
постоянные ошибки; блокировка бота одним пользователем не задерживает остальных.
При сбое между отправкой Telegram и сохранением message ID возможен дубль доставки.
Медиа, зависимости архивирования и cleanup расширят эту очередь в этапе 3.

Тесты покрывают QUEUE.md (включая примитивы возврата после будущей модерации),
повторный /start, имена, роли, старые callback, N1, конкуренцию, откат транзакции,
рестарт, миграцию с нуля/этапа 1 и отказ доставки. `Telegram.Bot` закреплён на
22.10.3.3; сигнатуры сверены с XML-документацией установленного пакета.

Секреты, БД, фото и логи исключены из Git/Docker context. Docker build и ручная
Telegram-репетиция не выполнялись. Пользовательский `PartyBot.Api.http` сохранён;
его шаблонные HTTP-запросы неприменимы к Generic Host.
