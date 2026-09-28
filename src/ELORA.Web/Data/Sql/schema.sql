-- ELORA — схема базы данных SQLite (elora.db)
-- Все даты/время хранятся как TEXT в формате ISO 8601 (yyyy-MM-ddTHH:mm:ss), локальное время студии.

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS AdminUsers (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Login        TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    CreatedAt    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ServiceCategories (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Slug        TEXT NOT NULL UNIQUE,
    Name        TEXT NOT NULL,
    Description TEXT,
    ImagePath   TEXT,
    PriceFrom   REAL NOT NULL DEFAULT 0,
    SortOrder   INTEGER NOT NULL DEFAULT 0,
    IsActive    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Services (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    CategoryId      INTEGER NOT NULL REFERENCES ServiceCategories(Id),
    Name            TEXT NOT NULL,
    Description     TEXT,
    DurationMinutes INTEGER NOT NULL,
    Price           REAL NOT NULL,
    SortOrder       INTEGER NOT NULL DEFAULT 0,
    IsActive        INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Masters (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Name           TEXT NOT NULL,
    Description    TEXT,
    Phone          TEXT,
    TelegramChatId TEXT,
    PhotoPath      TEXT,
    Specialization TEXT,
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    IsActive       INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS MasterServices (
    MasterId  INTEGER NOT NULL REFERENCES Masters(Id) ON DELETE CASCADE,
    ServiceId INTEGER NOT NULL REFERENCES Services(Id) ON DELETE CASCADE,
    PRIMARY KEY (MasterId, ServiceId)
);

CREATE TABLE IF NOT EXISTS WorkingHours (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    MasterId  INTEGER NOT NULL REFERENCES Masters(Id) ON DELETE CASCADE,
    DayOfWeek INTEGER NOT NULL,          -- 0 = воскресенье ... 6 = суббота
    StartTime TEXT NOT NULL,             -- HH:mm
    EndTime   TEXT NOT NULL,             -- HH:mm
    IsActive  INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS BlockedDates (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    MasterId INTEGER NULL REFERENCES Masters(Id) ON DELETE CASCADE,  -- NULL = блокировка для всех
    Date     TEXT NOT NULL,              -- yyyy-MM-dd
    Reason   TEXT
);

CREATE TABLE IF NOT EXISTS Clients (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Name           TEXT NOT NULL,
    Phone          TEXT NOT NULL,
    TelegramChatId TEXT,
    TelegramNick   TEXT,              -- @nick из поля «Ник в Telegram», если клиент его указал
    CreatedAt      TEXT NOT NULL,
    Notes          TEXT,
    LastRemindedAt TEXT              -- когда клиента в последний раз звали вернуться
);

CREATE TABLE IF NOT EXISTS Bookings (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId    INTEGER NOT NULL REFERENCES Clients(Id),
    MasterId    INTEGER NOT NULL REFERENCES Masters(Id),
    ServiceId   INTEGER NOT NULL REFERENCES Services(Id),
    StartAt     TEXT NOT NULL,
    EndAt       TEXT NOT NULL,
    Status      TEXT NOT NULL,
    Comment     TEXT,
    ManageToken TEXT NOT NULL DEFAULT '',
    CreatedAt   TEXT NOT NULL,
    UpdatedAt   TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Works (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Title       TEXT NOT NULL,
    Category    TEXT NOT NULL,
    ImagePath   TEXT NOT NULL,
    Description TEXT,
    MasterId    INTEGER NULL REFERENCES Masters(Id) ON DELETE SET NULL,
    SortOrder   INTEGER NOT NULL DEFAULT 0,
    IsActive    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Reviews (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientName TEXT NOT NULL,
    AvatarPath TEXT,
    Rating     INTEGER NOT NULL DEFAULT 5,
    Text       TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL,
    SortOrder  INTEGER NOT NULL DEFAULT 0,
    IsActive   INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS FAQ (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Question  TEXT NOT NULL,
    Answer    TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsActive  INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ReminderLogs (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    BookingId    INTEGER NOT NULL REFERENCES Bookings(Id) ON DELETE CASCADE,
    ReminderType TEXT NOT NULL,
    SentAt       TEXT NOT NULL
);

-- Одноразовые коды привязки Telegram: клиент переходит по deep-link и бот связывает chat_id.
CREATE TABLE IF NOT EXISTS TelegramLinkCodes (
    Code      TEXT PRIMARY KEY,
    ClientId  INTEGER NOT NULL REFERENCES Clients(Id) ON DELETE CASCADE,
    BookingId INTEGER NULL REFERENCES Bookings(Id) ON DELETE CASCADE,
    CreatedAt TEXT NOT NULL,
    UsedAt    TEXT
);

-- Несекретные настройки. Токен бота можно задать здесь (поля в админке) либо переменными
-- окружения Telegram__BotToken / Telegram__AdminBotToken — переменные окружения главнее.
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

-- Состояние шага диалога в боте: мастер переноса, новая запись, подтверждение отмены.
-- Живёт на сервере, потому что callback_data в кнопке ограничен 64 байтами и не вмещает
-- ни мастера, ни дату, ни время.
CREATE TABLE IF NOT EXISTS TelegramStates (
    ChatId    TEXT NOT NULL,
    Role      TEXT NOT NULL DEFAULT 'client',
    Step      TEXT NOT NULL,
    BookingId INTEGER NULL,
    MasterId  INTEGER NULL,
    Payload   TEXT NULL,
    UpdatedAt TEXT NOT NULL,
    PRIMARY KEY (ChatId, Role)
);

CREATE INDEX IF NOT EXISTS IX_Bookings_Master_StartAt ON Bookings (MasterId, StartAt);
CREATE INDEX IF NOT EXISTS IX_Bookings_ClientId       ON Bookings (ClientId);
CREATE INDEX IF NOT EXISTS IX_Bookings_Status         ON Bookings (Status);
CREATE INDEX IF NOT EXISTS IX_Bookings_ManageToken    ON Bookings (ManageToken);
CREATE INDEX IF NOT EXISTS IX_WorkingHours_MasterDay  ON WorkingHours (MasterId, DayOfWeek);
CREATE INDEX IF NOT EXISTS IX_BlockedDates_Date       ON BlockedDates (Date);
CREATE INDEX IF NOT EXISTS IX_Works_Category          ON Works (Category);
CREATE INDEX IF NOT EXISTS IX_Reviews_IsActive        ON Reviews (IsActive);
CREATE INDEX IF NOT EXISTS IX_FAQ_SortOrder           ON FAQ (SortOrder);
CREATE INDEX IF NOT EXISTS IX_Services_Category       ON Services (CategoryId);
CREATE INDEX IF NOT EXISTS IX_MasterServices_Service  ON MasterServices (ServiceId);
CREATE INDEX IF NOT EXISTS IX_TelegramStates_Updated  ON TelegramStates (UpdatedAt);
