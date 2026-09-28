# ELORA — Архитектура

## Общая схема

Browser
→ ASP.NET Core
→ Application Services
→ Data Access
→ SQLite

Telegram подключается через `TelegramService`.

## Архитектурный подход

Для MVP использовать один ASP.NET Core Web-проект.

Предпочтительно:

- Razor Pages для страниц;
- Services для бизнес-логики;
- Repository/Data Access для SQLite;
- Cookie Authentication для администратора.

Не помещать сложную бизнес-логику в PageModel/Controller.

## Слои

### Presentation

- Razor Pages / PageModels
- HTML/CSS/JavaScript
- validation и UI states

### Application

- BookingService
- ScheduleService
- AdminService
- TelegramService
- ReminderService

### Data

- SQLiteConnectionFactory
- repositories
- SQL schema
- seed/demo data

## Авторизация

Для `/Admin` использовать серверную Cookie Authentication.

Клиентские аккаунты не нужны.

Не использовать JWT + refresh token.

## Критическое состояние

Состояние записи всегда хранится в SQLite.

Нельзя полагаться только на JavaScript, in-memory коллекции или session.

## Telegram

Telegram — канал уведомлений и действий, а не база данных.

## Deployment

Локально:

- Visual Studio;
- F5/Ctrl+F5;
- браузер открывается через `launchSettings.json`.

Публично:

- `dotnet publish`;
- разместить publish output на ASP.NET Core-compatible hosting;
- настроить production connection/configuration;
- получить публичный URL;
- проверить HTTPS, БД, Telegram и фоновые напоминания.

SQLite требует сохранения файла БД между перезапусками/деплоями. Не использовать хостинг, где файловая система полностью эфемерная, если там не предусмотрено постоянное хранилище SQLite.

## Конфигурация

Секреты:

- Telegram Bot Token;
- admin credentials/секреты;

не хранить в git и исходниках. Использовать User Secrets для разработки и environment variables / secret configuration для production.
