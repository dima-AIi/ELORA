using System.Security.Cryptography;
using ELORA.Web.Models;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data.Repositories;

public sealed class BookingRepository
{
    private readonly SqliteConnectionFactory _factory;

    public BookingRepository(SqliteConnectionFactory factory) => _factory = factory;

    private const string BookingSelect = """
        SELECT b.Id, b.ClientId, b.MasterId, b.ServiceId, b.StartAt, b.EndAt, b.Status, b.Comment,
               b.ManageToken, b.CreatedAt, b.UpdatedAt,
               c.Name, c.Phone, c.TelegramChatId,
               m.Name, s.Name, cat.Name, s.DurationMinutes, s.Price,
               c.TelegramNick
        FROM Bookings b
        JOIN Clients c   ON c.Id = b.ClientId
        JOIN Masters m   ON m.Id = b.MasterId
        JOIN Services s  ON s.Id = b.ServiceId
        JOIN ServiceCategories cat ON cat.Id = s.CategoryId
        """;

    /// <summary>Активные записи мастера за конкретный день — база для расчёта свободных слотов.</summary>
    public List<Booking> GetActiveBookingsForMaster(int masterId, DateOnly date)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect +
            " WHERE b.MasterId = $m AND b.Status IN ('Pending','Confirmed')" +
            " AND b.StartAt >= $from AND b.StartAt < $to ORDER BY b.StartAt";
        command.With("$m", masterId)
               .With("$from", date.ToString("yyyy-MM-dd") + "T00:00:00")
               .With("$to", date.ToString("yyyy-MM-dd") + "T23:59:59");
        return Read(command);
    }

    public Booking? GetById(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect + " WHERE b.Id = $id";
        command.With("$id", id);
        return Read(command).FirstOrDefault();
    }

    public Booking? GetByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect + " WHERE b.ManageToken = $token";
        command.With("$token", token);
        return Read(command).FirstOrDefault();
    }

    /// <summary>
    /// Записи по списку токенов управления — для страницы «Мои записи»: браузер клиента
    /// помнит токены, по ним достаём сами записи. Пустой список — пустой результат,
    /// без запроса к базе.
    /// </summary>
    public List<Booking> GetByTokens(IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count == 0) return new List<Booking>();

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();

        var names = tokens.Select((_, index) => $"$t{index}").ToArray();
        command.CommandText = BookingSelect + $" WHERE b.ManageToken IN ({string.Join(", ", names)})";

        var position = 0;
        foreach (var token in tokens) command.With($"$t{position++}", token);

        return Read(command);
    }

    public List<Booking> GetByClientTelegram(string chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect +
            " WHERE c.TelegramChatId = $chat ORDER BY b.StartAt DESC LIMIT 20";
        command.With("$chat", chatId);
        return Read(command);
    }

    public List<Booking> GetBookings(DateOnly? from = null, DateOnly? to = null, string? status = null, int? masterId = null)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        var sql = BookingSelect + " WHERE 1 = 1";
        if (from.HasValue)
        {
            sql += " AND b.StartAt >= $from";
            command.With("$from", from.Value.ToString("yyyy-MM-dd") + "T00:00:00");
        }
        if (to.HasValue)
        {
            sql += " AND b.StartAt <= $to";
            command.With("$to", to.Value.ToString("yyyy-MM-dd") + "T23:59:59");
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND b.Status = $status";
            command.With("$status", status);
        }
        if (masterId.HasValue)
        {
            sql += " AND b.MasterId = $master";
            command.With("$master", masterId.Value);
        }
        sql += " ORDER BY b.StartAt DESC LIMIT 500";
        command.CommandText = sql;
        return Read(command);
    }

    public int CountByStatus(string status)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Bookings WHERE Status = $s";
        command.With("$s", status);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public decimal RevenueForPeriod(DateOnly from, DateOnly to)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(s.Price), 0) FROM Bookings b
            JOIN Services s ON s.Id = b.ServiceId
            WHERE b.Status IN ('Confirmed','Completed') AND b.StartAt >= $from AND b.StartAt <= $to
            """;
        command.With("$from", from.ToString("yyyy-MM-dd") + "T00:00:00")
               .With("$to", to.ToString("yyyy-MM-dd") + "T23:59:59");
        return Convert.ToDecimal(command.ExecuteScalar());
    }

    /// <summary>
    /// Находит клиента по нормализованному телефону или заводит нового.
    /// </summary>
    /// <param name="telegramChatId">
    /// Известный чат в Telegram. <c>null</c> означает «не трогать сохранённый»: клиент,
    /// однажды написавший боту, не должен потерять привязку из-за записи с сайта.
    /// </param>
    /// <param name="telegramNick">
    /// Ник из поля «Ник в Telegram», уже приведённый к виду <c>@nick</c>. Пустая строка
    /// не стирает прежний ник: клиент мог просто не заполнять поле в этот раз.
    /// </param>
    public int GetOrCreateClient(
        SqliteConnection connection, SqliteTransaction? transaction,
        string name, string phone, string? telegramChatId, string? telegramNick = null)
    {
        var normalized = NormalizePhone(phone);

        var existingId = FindClientId(connection, transaction, normalized);
        if (existingId is int id)
        {
            UpdateClient(connection, transaction, id, name, telegramChatId, telegramNick);
            return id;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Clients (Name, Phone, TelegramChatId, TelegramNick, CreatedAt)
            VALUES ($name, $phone, $chat, $nick, $now);
            SELECT last_insert_rowid();
            """;
        insert.With("$name", name).With("$phone", normalized).With("$chat", telegramChatId)
              .With("$nick", string.IsNullOrWhiteSpace(telegramNick) ? null : telegramNick)
              .With("$now", DateTime.Now.ToString("s"));
        return Convert.ToInt32(insert.ExecuteScalar());
    }

    /// <summary>
    /// Ищет клиента по телефону: сначала точным совпадением, потом перебором по цифрам.
    /// </summary>
    /// <remarks>
    /// Точное совпадение не находит строки, записанные до приведения телефона к единому
    /// виду: старый код просто приписывал «+» к цифрам, поэтому в базе лежит, например,
    /// <c>+89583335555</c> там, где теперь ищется <c>+78958333555</c>. Клиентов у студии
    /// немного, и перебор дешевле раздвоения карточек: у второй карточки не было бы чата
    /// Telegram, и подтверждения о записях человеку не доходили бы. Найденную строку
    /// заодно лечим, чтобы в следующий раз хватило точного совпадения.
    /// </remarks>
    private static int? FindClientId(
        SqliteConnection connection, SqliteTransaction? transaction, string normalized)
    {
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT Id FROM Clients WHERE Phone = $phone LIMIT 1";
            find.With("$phone", normalized);
            if (find.ExecuteScalar() is long exact) return (int)exact;
        }

        using var scan = connection.CreateCommand();
        scan.Transaction = transaction;
        scan.CommandText = "SELECT Id, Phone FROM Clients";
        using var reader = scan.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(1)) continue;
            if (!string.Equals(NormalizePhone(reader.GetString(1)), normalized, StringComparison.Ordinal))
                continue;

            var id = reader.GetInt32(0);
            reader.Close();

            using var heal = connection.CreateCommand();
            heal.Transaction = transaction;
            heal.CommandText = "UPDATE Clients SET Phone = $phone WHERE Id = $id";
            heal.With("$phone", normalized).With("$id", id);
            heal.ExecuteNonQuery();
            return id;
        }

        return null;
    }

    private static void UpdateClient(
        SqliteConnection connection, SqliteTransaction? transaction, int id,
        string name, string? telegramChatId, string? telegramNick)
    {
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            "UPDATE Clients SET Name = $name, " +
            "TelegramChatId = COALESCE($chat, TelegramChatId), " +
            "TelegramNick = COALESCE($nick, TelegramNick) WHERE Id = $id";
        update.With("$name", name)
              .With("$chat", telegramChatId)
              .With("$nick", string.IsNullOrWhiteSpace(telegramNick) ? null : telegramNick)
              .With("$id", id);
        update.ExecuteNonQuery();
    }

    /// <summary>
    /// Приводит телефон к одному виду: <c>+7XXXXXXXXXX</c>.
    /// </summary>
    /// <remarks>
    /// Единый вид — не косметика, а защита от раздвоения клиентов. Телефон приходит из двух
    /// мест: с сайта (там маска всегда даёт «+7 (900) 000-00-11») и из бота, где человек
    /// пишет номер как привык — «89000000011» или «9000000011». Без приведения кода страны
    /// один и тот же человек заводился бы дважды: карточка с сайта знала бы его чат в Telegram,
    /// а карточка из бота — нет, и подтверждения о записях клиенту не доходили бы.
    /// </remarks>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return phone.Trim();

        if (digits.Length == 11 && digits[0] == '8')
            digits = "7" + digits[1..];      // 8XXXXXXXXXX — старый код страны
        else if (digits.Length == 10)
            digits = "7" + digits;           // номер без кода страны

        return "+" + digits;
    }

    public void UpdateStatus(int bookingId, string status)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Bookings SET Status = $status, UpdatedAt = $now WHERE Id = $id";
        command.With("$status", status).With("$now", DateTime.Now.ToString("s")).With("$id", bookingId);
        command.ExecuteNonQuery();
    }

    public void UpdateTime(int bookingId, DateTime start, DateTime end)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Bookings SET StartAt = $start, EndAt = $end, UpdatedAt = $now WHERE Id = $id";
        command.With("$start", start.ToString("s")).With("$end", end.ToString("s"))
               .With("$now", DateTime.Now.ToString("s")).With("$id", bookingId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Удаляет запись совсем. Нужно владельцу: тестовую запись или явную ошибку иначе
    /// не убрать из списка — отменить можно, а удалить нет, и они копятся в базе.
    /// Напоминания и коды привязки уходят каскадом (в соединении включён
    /// <c>PRAGMA foreign_keys = ON</c>), а шаг диалога в боте приходится чистить руками:
    /// внешнего ключа у <c>TelegramStates.BookingId</c> нет.
    /// </summary>
    public bool Delete(int bookingId)
    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        using (var state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = "UPDATE TelegramStates SET BookingId = NULL WHERE BookingId = $id";
            state.With("$id", bookingId);
            state.ExecuteNonQuery();
        }

        int affected;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Bookings WHERE Id = $id";
            command.With("$id", bookingId);
            affected = command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected > 0;
    }

    public void SetClientTelegram(int clientId, string chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Clients SET TelegramChatId = $chat WHERE Id = $id";
        command.With("$chat", chatId).With("$id", clientId);
        command.ExecuteNonQuery();
    }

    /// <summary>Привязан ли этот чат хоть к одной карточке клиента.</summary>
    public bool IsChatBound(string chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Clients WHERE TelegramChatId = $chat";
        command.With("$chat", chatId);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Привязывает чат к клиенту по номеру телефона — тот же результат, что у
    /// <see cref="SetClientTelegram"/>, но клиента ещё надо найти.
    /// </summary>
    /// <remarks>
    /// Нужно кнопке «отправить мой номер» в клиентском боте. Ссылка привязки закрывается
    /// вместе со страницей успешной записи, и человек, открывший бота сам, до привязки
    /// не доходил вовсе — а без неё бот не может написать первым, и подтверждения
    /// о записях ему не уходили.
    /// <para>
    /// Если клиента с таким номером ещё нет, заводим карточку сразу с чатом. Человек
    /// поделился номером сам, и первая же запись с сайта найдёт эту карточку по телефону
    /// (<see cref="GetOrCreateClient"/>) — уже с привязанным чатом. Иначе пришлось бы просить
    /// его привязываться второй раз, чего он уже не сделает.
    /// </para>
    /// </remarks>
    public int BindChatByPhone(string phone, string chatId, string name)
    {
        var normalized = NormalizePhone(phone);

        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        var existingId = FindClientId(connection, transaction, normalized);
        if (existingId is int id)
        {
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Clients SET TelegramChatId = $chat WHERE Id = $id";
                update.With("$chat", chatId).With("$id", id);
                update.ExecuteNonQuery();
            }

            transaction.Commit();
            return id;
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Clients (Name, Phone, TelegramChatId, CreatedAt, Notes)
            VALUES ($name, $phone, $chat, $now, $note);
            SELECT last_insert_rowid();
            """;
        insert.With("$name", string.IsNullOrWhiteSpace(name) ? "Клиент из Telegram" : name.Trim())
              .With("$phone", normalized)
              .With("$chat", chatId)
              .With("$now", DateTime.Now.ToString("s"))
              .With("$note", "Привязал чат в боте, записей пока нет");

        var created = Convert.ToInt32(insert.ExecuteScalar());
        transaction.Commit();
        return created;
    }

    // --- Напоминания ---

    public bool HasReminder(int bookingId, string type)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReminderLogs WHERE BookingId = $id AND ReminderType = $type";
        command.With("$id", bookingId).With("$type", type);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public void AddReminder(int bookingId, string type)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ReminderLogs (BookingId, ReminderType, SentAt) VALUES ($id, $type, $now)";
        command.With("$id", bookingId).With("$type", type).With("$now", DateTime.Now.ToString("s"));
        command.ExecuteNonQuery();
    }

    /// <summary>Записи, до начала которых осталось меньше указанного окна (для напоминаний).</summary>
    public List<Booking> GetUpcomingForReminder(DateTime from, DateTime to)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect +
            " WHERE b.Status IN ('Pending','Confirmed') AND b.StartAt >= $from AND b.StartAt <= $to" +
            " ORDER BY b.StartAt";
        command.With("$from", from.ToString("s")).With("$to", to.ToString("s"));
        return Read(command);
    }

    // --- Коды привязки Telegram ---

    public void CreateLinkCode(string code, int clientId, int? bookingId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO TelegramLinkCodes (Code, ClientId, BookingId, CreatedAt, UsedAt) " +
            "VALUES ($code, $client, $booking, $now, NULL)";
        command.With("$code", code).With("$client", clientId).With("$booking", bookingId)
               .With("$now", DateTime.Now.ToString("s"));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Код привязки для клиента: возвращает уже выданный неиспользованный либо создаёт новый.
    /// Нужен странице успеха — она открывается отдельным GET после записи, и код из ответа
    /// POST до неё не доходит, поэтому ссылку на бота приходилось строить без параметра start.
    /// </summary>
    public string GetOrCreateLinkCode(int clientId, int? bookingId)
    {
        using var connection = _factory.Create();

        using (var select = connection.CreateCommand())
        {
            select.CommandText =
                "SELECT Code FROM TelegramLinkCodes WHERE ClientId = $client AND UsedAt IS NULL " +
                "ORDER BY CreatedAt DESC LIMIT 1";
            select.With("$client", clientId);
            if (select.ExecuteScalar() is string existing && !string.IsNullOrWhiteSpace(existing))
                return existing;
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        CreateLinkCode(code, clientId, bookingId);
        return code;
    }

    /// <summary>Помечает код использованным и возвращает связанные идентификаторы.</summary>
    public (int ClientId, int? BookingId)? ConsumeLinkCode(string code)    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        int clientId;
        int? bookingId;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT ClientId, BookingId FROM TelegramLinkCodes WHERE Code = $code AND UsedAt IS NULL";
            select.With("$code", code);
            using var reader = select.ExecuteReader();
            if (!reader.Read()) return null;
            clientId = reader.Int(0);
            bookingId = reader.IntOrNull(1);
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE TelegramLinkCodes SET UsedAt = $now WHERE Code = $code";
            update.With("$now", DateTime.Now.ToString("s")).With("$code", code);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return (clientId, bookingId);
    }

    public List<Booking> GetActiveStartingSoon(DateTime from, DateTime to)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect +
            " WHERE b.Status IN ('Pending','Confirmed') AND b.StartAt >= $from AND b.StartAt <= $to";
        command.With("$from", from.ToString("s")).With("$to", to.ToString("s"));
        return Read(command);
    }

    public List<Booking> GetStalePending(DateTime olderThan)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = BookingSelect + " WHERE b.Status = 'Pending' AND b.CreatedAt < $t";
        command.With("$t", olderThan.ToString("s"));
        return Read(command);
    }

    public int CountAll()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Bookings";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<Booking> Read(SqliteCommand command)
    {
        var list = new List<Booking>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Booking
            {
                Id = reader.Int(0),
                ClientId = reader.Int(1),
                MasterId = reader.Int(2),
                ServiceId = reader.Int(3),
                StartAt = reader.Str(4),
                EndAt = reader.Str(5),
                Status = reader.Str(6),
                Comment = reader.StrOrNull(7),
                ManageToken = reader.Str(8),
                CreatedAt = reader.Str(9),
                UpdatedAt = reader.Str(10),
                ClientName = reader.Str(11),
                ClientPhone = reader.Str(12),
                ClientTelegramChatId = reader.StrOrNull(13),
                MasterName = reader.Str(14),
                ServiceName = reader.Str(15),
                ServiceCategory = reader.Str(16),
                DurationMinutes = reader.Int(17),
                Price = reader.Decimal(18),
                ClientTelegramNick = reader.StrOrNull(19)
            });
        }
        return list;
    }
}
