# PartyBot

Telegram-бот одной Halloween-вечеринки. Реализован этап 1: .NET 10 Generic Host,
конфигурация, EF Core/SQLite migrations и xUnit-инфраструктура. Игровые команды,
polling и доставка пока не запускаются; токен для локальной разработки не нужен.
Спецификации и порядок следующих этапов — в [docs/README.md](docs/README.md).

## Локальный запуск

Нужен .NET SDK 10.0.400 (или более новый patch этой feature band). Команды из корня репозитория:

```sh
dotnet restore PartyBot.slnx
dotnet tool restore
dotnet build PartyBot.slnx --no-restore
dotnet test PartyBot.slnx --no-build
dotnet run --project PartyBot.Api --launch-profile Bootstrap
```

Приложение применяет миграции, создаёт инфраструктурную отметку и ждёт Ctrl+C.
Никакие запросы в Telegram не выполняются. При повторном старте данные сохраняются.
Профиль Bootstrap задаёт `DOTNET_ENVIRONMENT=Development`.

Проверить только миграцию и завершиться:

```sh
dotnet run --project PartyBot.Api --launch-profile Bootstrap -- --migrate-only
dotnet ef migrations list --project PartyBot.Api
dotnet ef migrations has-pending-model-changes --project PartyBot.Api
```

Для отдельной чистой БД выберите новый путь:

```sh
Bot__DatabasePath=/tmp/partybot-bootstrap-check.db dotnet run --project PartyBot.Api --launch-profile Bootstrap -- --migrate-only
```

EF CLI также поддерживает `Bot__DatabasePath`; `dotnet ef database update --project PartyBot.Api`
применяет схему без запуска host (инфраструктурная отметка создастся при запуске приложения).
Тесты самостоятельно создают и удаляют отдельные временные SQLite-файлы.

## Конфигурация

`appsettings.json` → `appsettings.{Environment}.json` → user-secrets (Development)
→ переменные окружения → аргументы командной строки. Конфигурация и относительные
пути БД/каталога разрешаются от каталога исполняемого приложения. Например, в Debug
БД по умолчанию лежит в `PartyBot.Api/bin/Debug/net10.0/data/partybot.db`.

- `Bot__BotToken`: секрет, хранить через environment или .NET user-secrets с ключом `Bot:BotToken`.
- `Bot__SuperAdminUserId`, `Bot__OrganizerUserIds__0`, `Bot__OrganizerUserIds__1`: числовые Telegram user IDs.
- `Bot__ModerationChatId`, `Bot__ArchiveChatId`, `Bot__AuditChatId`, необязательный `Bot__AuditThreadId`.
- `Bot__SkipCooldownMinutes`, `Bot__SubmitCooldownMinutes`: N1/N2. Только Development использует placeholders 1/1.
- `Bot__DatabasePath`: в production обязателен абсолютный путь на постоянном доступном для записи диске.
- `Bot__CatalogPath`: пока демонстрационный `Content/catalog.demo.json`, без импорта в БД.
- `Bot__DisplayTimeZone`: Europe/Moscow. В SQLite время хранится как INTEGER UTC ticks.

Вне Development пустые токен/ID и неуказанные N1/N2 отклоняются. Проверка настроек
не является проверкой доступа к Telegram. Проверка каталога и готовности к Open —
следующие этапы. Не переносите демонстрационные задания в реальную игру.

## Структура и тестирование

Один основной проект `PartyBot.Api` (историческое имя; HTTP сервера больше нет),
один `PartyBot.Tests`. `Configuration`, `Persistence`, `Infrastructure`, `Telegram`,
`Content` разделяют ответственность. Модель игры добавляется в следующих этапах.

`TimeProvider` и `IRandomSource` внедряются через DI. Тесты используют FakeTimeProvider,
SeededRandomSource и FakeTelegramGateway с записью отправок и имитацией ошибки.
Интеграционные тесты работают с реальной SQLite: миграции, рестарт, ограничения,
UTC-сравнения. Bootstrap не регистрирует реальный TelegramGateway.

`Telegram.Bot` закреплён на 22.10.3.3; минимальный адаптер компилируется с
`ITelegramBotClient.SendMessage` ([исходники SDK](https://github.com/TelegramBots/Telegram.Bot)).

Секреты, SQLite-файлы, фото и логи исключены из Git и Docker context. Существующий
`PartyBot.Api.http` сохранён как пользовательский файл, его шаблонные HTTP-запросы
к bootstrap неприменимы. Dockerfile обновлён под Generic Host; deployment и ручная
Telegram-репетиция пока не проверены.
