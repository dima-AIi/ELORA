# ELORA — SQLite Database

Файл: `elora.db`.

## AdminUsers

Id INTEGER PK  
Login TEXT UNIQUE NOT NULL  
PasswordHash TEXT NOT NULL  
CreatedAt TEXT NOT NULL

## Services

Id INTEGER PK  
Name TEXT NOT NULL  
Description TEXT  
DurationMinutes INTEGER NOT NULL  
Price REAL NOT NULL  
IsActive INTEGER NOT NULL

## Masters

Id INTEGER PK  
Name TEXT NOT NULL  
Description TEXT  
Phone TEXT  
TelegramChatId TEXT  
IsActive INTEGER NOT NULL

## MasterServices

MasterId INTEGER FK Masters(Id)  
ServiceId INTEGER FK Services(Id)  
PRIMARY KEY (MasterId, ServiceId)

## WorkingHours

Id INTEGER PK  
MasterId INTEGER FK Masters(Id)  
DayOfWeek INTEGER NOT NULL  
StartTime TEXT NOT NULL  
EndTime TEXT NOT NULL  
IsActive INTEGER NOT NULL

## BlockedDates

Id INTEGER PK  
MasterId INTEGER NULL FK Masters(Id)  
Date TEXT NOT NULL  
Reason TEXT

MasterId NULL означает блокировку для всех.

## Clients

Id INTEGER PK  
Name TEXT NOT NULL  
Phone TEXT NOT NULL  
TelegramChatId TEXT  
CreatedAt TEXT NOT NULL

## Bookings

Id INTEGER PK  
ClientId INTEGER FK Clients(Id)  
MasterId INTEGER FK Masters(Id)  
ServiceId INTEGER FK Services(Id)  
StartAt TEXT NOT NULL  
EndAt TEXT NOT NULL  
Status TEXT NOT NULL  
CreatedAt TEXT NOT NULL  
UpdatedAt TEXT NOT NULL

Статусы:

- Pending
- Confirmed
- Cancelled
- Completed

## Works

Id INTEGER PK  
Title TEXT NOT NULL  
Category TEXT NOT NULL  
ImagePath TEXT NOT NULL  
Description TEXT  
MasterId INTEGER NULL FK Masters(Id)  
IsActive INTEGER NOT NULL

Категории:

Manicure, Pedicure, Lashes, Brows, Hair, Tattoo.

## Reviews

Id INTEGER PK  
ClientName TEXT NOT NULL  
AvatarPath TEXT  
Rating INTEGER NOT NULL  
Text TEXT NOT NULL  
CreatedAt TEXT NOT NULL  
IsActive INTEGER NOT NULL

## FAQ

Id INTEGER PK  
Question TEXT NOT NULL  
Answer TEXT NOT NULL  
SortOrder INTEGER NOT NULL  
IsActive INTEGER NOT NULL

## ReminderLogs

Id INTEGER PK  
BookingId INTEGER FK Bookings(Id)  
ReminderType TEXT NOT NULL  
SentAt TEXT NOT NULL

## Индексы

Bookings(MasterId, StartAt)  
Bookings(ClientId)  
Bookings(Status)  
WorkingHours(MasterId, DayOfWeek)  
BlockedDates(Date)  
Works(Category)  
Reviews(IsActive)  
FAQ(SortOrder)

Включить SQLite foreign keys через:

`PRAGMA foreign_keys = ON`.

Старые услуги/мастера не удалять физически, если есть история: использовать `IsActive`.
