using System.Security.Cryptography;
using ELORA.Web.Data;
using ELORA.Web.Data.Repositories;
using ELORA.Web.DTOs;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Services;

/// <summary>
/// Создание, отмена и перенос записей.
/// Проверка занятости выполняется на сервере внутри SQLite-транзакции
/// (BEGIN IMMEDIATE → поиск пересечений → INSERT → COMMIT), поэтому
/// двойная запись на один слот невозможна даже при параллельных запросах.
/// </summary>
public sealed class BookingService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly CatalogRepository _catalog;
    private readonly MasterRepository _masters;
    private readonly BookingRepository _bookings;
    private readonly ScheduleService _schedule;
    private readonly TelegramService _telegram;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        SqliteConnectionFactory factory,
        CatalogRepository catalog,
        MasterRepository masters,
        BookingRepository bookings,
        ScheduleService schedule,
        TelegramService telegram,
        ILogger<BookingService> logger)
    {
        _factory = factory;
        _catalog = catalog;
        _masters = masters;
        _bookings = bookings;
        _schedule = schedule;
        _telegram = telegram;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт запись и рассылает уведомления.
    /// </summary>
    /// <param name="notifyClient">
    /// Отправлять ли подтверждение клиенту в клиентский бот. Клиентский диалог-мастер
    /// (<c>/book</c> в боте) передаёт <c>false</c>: он и так показывает «Вы записаны!»
    /// в том же чате, и второе сообщение было бы дублем.
    /// </param>
    public async Task<BookingResult> CreateAsync(
        BookingRequest request, CancellationToken cancellationToken = default, bool notifyClient = true)
    {
        var validation = ValidateRequest(request);
        if (validation is not null) return new BookingResult { Ok = false, Error = validation };

        var service = _catalog.GetService(request.ServiceId);
        var master = _masters.GetById(request.MasterId);
        if (service is null || master is null)
            return new BookingResult { Ok = false, Error = "Услуга или мастер не найдены" };

        if (!DateOnly.TryParse(request.Date, out var date))
            return new BookingResult { Ok = false, Error = "Некорректная дата" };

        if (!TimeOnly.TryParse(request.Time, out var time))
            return new BookingResult { Ok = false, Error = "Некорректное время" };

        // Повторная серверная валидация слотов (не доверяем данным из браузера).
        var slots = _schedule.GetSlots(request.ServiceId, request.MasterId, date);
        if (!slots.Ok) return new BookingResult { Ok = false, Error = slots.Error };
        if (slots.Slots.All(s => s.Time != request.Time))
            return new BookingResult { Ok = false, Error = "Это время уже занято. Выберите другой слот." };

        var start = date.ToDateTime(time);
        var end = start.AddMinutes(service.DurationMinutes);

        string manageToken = GenerateToken();
        int bookingId;

        // Ник приводим к виду @nick до записи в базу. Всё, что ником не является (имя,
        // телефон, «мой телеграм», ссылка на профиль с мусором), превращается в null:
        // пустое поле честнее, чем «@что угодно» в карточке клиента.
        var telegramNick = TelegramNick.Normalize(request.Telegram);

        using (var connection = _factory.Create())
        {
            // deferred: false → BEGIN IMMEDIATE: блокировка на запись берётся сразу,
            // поэтому конкурентная вставка в тот же интервал не пройдёт.
            using var transaction = connection.BeginTransaction(deferred: false);

            var conflict = FindConflict(connection, transaction, request.MasterId, start, end, null);
            if (conflict is not null)
            {
                transaction.Rollback();
                return new BookingResult { Ok = false, Error = "Это время уже занято. Выберите другой слот." };
            }

            var clientId = _bookings.GetOrCreateClient(
                connection, transaction, request.Name.Trim(), request.Phone.Trim(), null, telegramNick);

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Bookings (ClientId, MasterId, ServiceId, StartAt, EndAt, Status, Comment,
                                      ManageToken, CreatedAt, UpdatedAt)
                VALUES ($client, $master, $service, $start, $end, $status, $comment, $token, $now, $now);
                SELECT last_insert_rowid();
                """;
            insert.With("$client", clientId)
                  .With("$master", request.MasterId)
                  .With("$service", request.ServiceId)
                  .With("$start", start.ToString("s"))
                  .With("$end", end.ToString("s"))
                  .With("$status", BookingStatuses.Pending)
                  .With("$comment", string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim())
                  .With("$token", manageToken)
                  .With("$now", DateTime.Now.ToString("s"));

            bookingId = Convert.ToInt32(insert.ExecuteScalar());
            transaction.Commit();
        }

        var booking = _bookings.GetById(bookingId);
        if (booking is not null)
        {
            // Уведомления отправляются после COMMIT: если Telegram недоступен,
            // запись всё равно остаётся созданной.
            await SafeNotifyAsync(
                () => _telegram.NotifyNewBookingAsync(booking, notifyClient, cancellationToken), cancellationToken);
        }

        // Ссылку привязки выдаём всем, а не только тем, кто вписал ник в Telegram. Ник —
        // это подпись в карточке клиента, а привязка чата нужна каждому: без неё клиент
        // не получит ни подтверждения, ни напоминания, потому что бот не может написать первым.
        var link = "";
        if (booking is not null)
        {
            var code = GenerateToken(8);
            _bookings.CreateLinkCode(code, booking.ClientId, booking.Id);
            link = _telegram.BuildStartLink(code);
        }

        return new BookingResult
        {
            Ok = true,
            BookingId = bookingId,
            ManageToken = manageToken,
            TelegramStartLink = link,
            Summary = booking is null
                ? ""
                : $"{booking.ServiceName} · {booking.MasterName} · {start:dd.MM.yyyy HH:mm}"
        };
    }

    /// <summary>
    /// Меняет статус записи и уведомляет клиента.
    /// </summary>
    /// <remarks>
    /// Единая точка для админки: кнопки «Подтвердить» и «Завершить» меняли статус молча,
    /// и клиент узнавал о подтверждении только из бота. Отмену отправляем через
    /// <see cref="CancelAsync"/> — там своё сообщение и своя проверка «уже отменена».
    /// </remarks>
    /// <returns>Причина отказа словами либо <c>null</c> при успехе.</returns>
    public async Task<string?> SetStatusAsync(int bookingId, string status, CancellationToken cancellationToken = default)
    {
        if (!BookingStatuses.All.Contains(status)) return "Неизвестный статус";

        var booking = _bookings.GetById(bookingId);
        if (booking is null) return "Запись не найдена";

        if (status == BookingStatuses.Cancelled)
        {
            var cancelled = await CancelAsync(booking.ManageToken, byAdmin: true, cancellationToken);
            return cancelled.Ok ? null : cancelled.Error;
        }

        // Статус не изменился — сообщать нечего, и повторное «подтверждено» только путает.
        if (booking.Status == status) return null;

        _bookings.UpdateStatus(bookingId, status);

        var updated = _bookings.GetById(bookingId);
        if (updated is not null)
            await SafeNotifyAsync(() => _telegram.NotifyStatusChangedAsync(updated, cancellationToken), cancellationToken);

        return null;
    }

    public async Task<BookingResult> CancelAsync(string manageToken, bool byAdmin = false, CancellationToken cancellationToken = default)
    {
        var booking = _bookings.GetByToken(manageToken);
        if (booking is null) return new BookingResult { Ok = false, Error = "Запись не найдена" };
        if (booking.Status == BookingStatuses.Cancelled)
            return new BookingResult { Ok = false, Error = "Запись уже отменена" };
        if (booking.Status == BookingStatuses.Completed)
            return new BookingResult { Ok = false, Error = "Завершённую запись нельзя отменить" };

        _bookings.UpdateStatus(booking.Id, BookingStatuses.Cancelled);

        var updated = _bookings.GetById(booking.Id);
        if (updated is not null)
            await SafeNotifyAsync(() => _telegram.NotifyCancelledAsync(updated, byAdmin, cancellationToken), cancellationToken);

        return new BookingResult { Ok = true, BookingId = booking.Id, ManageToken = manageToken };
    }

    public async Task<BookingResult> RescheduleAsync(
        string manageToken, int masterId, string date, string time, CancellationToken cancellationToken = default)
    {
        var booking = _bookings.GetByToken(manageToken);
        if (booking is null) return new BookingResult { Ok = false, Error = "Запись не найдена" };
        if (booking.Status is BookingStatuses.Cancelled or BookingStatuses.Completed)
            return new BookingResult { Ok = false, Error = "Эту запись нельзя перенести" };

        if (!DateOnly.TryParse(date, out var parsedDate) || !TimeOnly.TryParse(time, out var parsedTime))
            return new BookingResult { Ok = false, Error = "Некорректные дата или время" };

        var slots = _schedule.GetSlots(booking.ServiceId, masterId, parsedDate);
        if (!slots.Ok) return new BookingResult { Ok = false, Error = slots.Error };
        if (slots.Slots.All(s => s.Time != time))
            return new BookingResult { Ok = false, Error = "Это время уже занято. Выберите другой слот." };

        var start = parsedDate.ToDateTime(parsedTime);
        var end = start.AddMinutes(booking.DurationMinutes);

        using (var connection = _factory.Create())
        {
            using var transaction = connection.BeginTransaction(deferred: false);

            var conflict = FindConflict(connection, transaction, masterId, start, end, booking.Id);
            if (conflict is not null)
            {
                transaction.Rollback();
                return new BookingResult { Ok = false, Error = "Это время уже занято. Выберите другой слот." };
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Bookings SET MasterId = $master, StartAt = $start, EndAt = $end, UpdatedAt = $now
                WHERE Id = $id
                """;
            update.With("$master", masterId)
                  .With("$start", start.ToString("s"))
                  .With("$end", end.ToString("s"))
                  .With("$now", DateTime.Now.ToString("s"))
                  .With("$id", booking.Id);
            update.ExecuteNonQuery();

            transaction.Commit();
        }

        var updated = _bookings.GetById(booking.Id);
        if (updated is not null)
            await SafeNotifyAsync(() => _telegram.NotifyRescheduledAsync(updated, cancellationToken), cancellationToken);

        return new BookingResult
        {
            Ok = true,
            BookingId = booking.Id,
            ManageToken = manageToken,
            Summary = $"{start:dd.MM.yyyy HH:mm}"
        };
    }

    /// <summary>Ищет активную запись, пересекающуюся с интервалом. Отменённые записи слот не блокируют.</summary>
    private static int? FindConflict(
        SqliteConnection connection, SqliteTransaction transaction,
        int masterId, DateTime start, DateTime end, int? excludeBookingId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id FROM Bookings
            WHERE MasterId = $master
              AND Status IN ('Pending','Confirmed')
              AND StartAt < $end AND EndAt > $start
              AND ($exclude IS NULL OR Id <> $exclude)
            LIMIT 1
            """;
        command.With("$master", masterId)
               .With("$start", start.ToString("s"))
               .With("$end", end.ToString("s"))
               .With("$exclude", excludeBookingId);

        var result = command.ExecuteScalar();
        return result is long id ? (int)id : null;
    }

    private static string? ValidateRequest(BookingRequest request)
    {
        if (request.ServiceId <= 0) return "Выберите услугу";
        if (request.MasterId <= 0) return "Выберите мастера";
        if (string.IsNullOrWhiteSpace(request.Date)) return "Выберите дату";
        if (string.IsNullOrWhiteSpace(request.Time)) return "Выберите время";

        // Проверки имени, телефона, комментария и ника вынесены в общий модуль
        // UserInput: те же правила применяются и в боте, и в панели управления,
        // поэтому менять их нужно в одном месте.
        var nameError = UserInput.ValidateName(request.Name);
        if (nameError is not null) return nameError;

        var phoneError = UserInput.ValidatePhone(request.Phone);
        if (phoneError is not null) return phoneError;

        var commentError = UserInput.ValidateComment(request.Comment);
        if (commentError is not null) return commentError;

        // Ник в Telegram необязателен: пустое поле — это нормально, а вот
        // неправильный ник раньше просто молча исчезал. Теперь человек узнаёт
        // причину сразу.
        var nickError = UserInput.ValidateTelegramNick(request.Telegram);
        if (nickError is not null) return nickError;

        return null;
    }

    public static string GenerateToken(int bytes = 16) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private async Task SafeNotifyAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить уведомление Telegram");
        }
    }
}
