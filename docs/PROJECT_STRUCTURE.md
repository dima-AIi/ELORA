# ELORA — Структура проекта

ELORA.sln
└── ELORA.Web

ELORA.Web/
├── Areas/
│   └── Admin/
│       └── Pages/
├── Pages/
│   ├── Index.cshtml
│   ├── Services.cshtml
│   ├── Works.cshtml
│   ├── Price.cshtml
│   ├── Booking.cshtml
│   ├── BookingSuccess.cshtml
│   ├── About.cshtml
│   ├── Contacts.cshtml
│   └── FAQ.cshtml
├── Models/
├── DTOs/
├── Services/
│   ├── BookingService.cs
│   ├── ScheduleService.cs
│   ├── TelegramService.cs
│   ├── ReminderService.cs
│   ├── AdminService.cs
│   └── WorksService.cs
├── Data/
│   ├── SQLiteConnectionFactory.cs
│   ├── Repositories/
│   └── Sql/
│       └── schema.sql
├── Background/
│   └── ReminderBackgroundService.cs
├── wwwroot/
│   ├── css/
│   │   └── site.css
│   ├── js/
│   │   ├── site.js
│   │   └── booking.js
│   └── images/
│       ├── hero/
│       ├── services/
│       ├── works/
│       ├── reviews/
│       └── promo/
├── appsettings.json
├── appsettings.Development.json
├── Properties/
│   └── launchSettings.json
├── Program.cs
└── ELORA.Web.csproj

Один ASP.NET Core проект достаточен.

Секрет Telegram Bot Token не хранить в исходниках: User Secrets / environment variables.

Логи: встроенный `ILogger`.
