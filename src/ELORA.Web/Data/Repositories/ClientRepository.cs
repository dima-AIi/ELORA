using ELORA.Web.Helpers;
using ELORA.Web.Models;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data.Repositories;

/// <summary>
/// Клиенты студии с агрегатами по визитам. До этого раздела в админке не было: клиентов
/// можно было увидеть только через их записи, и то по одной.
/// </summary>
public sealed class ClientRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ClientRepository(SqliteConnectionFactory factory) => _factory = factory;

    /// <summary>
    /// Считаем только завершённые визиты: отменённая запись — не визит, и деньги за неё
    /// студия не получила.
    /// </summary>
    private const string ClientSelect = """
        SELECT c.Id, c.Name, c.Phone, c.TelegramChatId, c.Notes, c.CreatedAt, c.LastRemindedAt,
               COALESCE(SUM(CASE WHEN b.Status = 'Completed' THEN 1 ELSE 0 END), 0) AS Visits,
               MAX(CASE WHEN b.Status = 'Completed' THEN b.StartAt END)               AS LastVisit,
               COALESCE(SUM(CASE WHEN b.Status = 'Completed' THEN s.Price ELSE 0 END), 0) AS TotalSpent,
               COALESCE(MAX(CASE WHEN b.Status IN ('Pending','Confirmed') AND b.StartAt >= $now
                                 THEN b.StartAt END), '')                          AS NextVisit,
               c.TelegramNick
        FROM Clients c
        LEFT JOIN Bookings b ON b.ClientId = c.Id
        LEFT JOIN Services s ON s.Id = b.ServiceId
        """;

    /// <summary>Список клиентов с поиском по имени и телефону.</summary>
    public List<ClientSummary> GetAll(string? search = null)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();

        var sql = ClientSelect;
        if (!string.IsNullOrWhiteSpace(search))
            sql += " WHERE c.Name LIKE $q OR c.Phone LIKE $q";

        sql += " GROUP BY c.Id ORDER BY (NextVisit = '') ASC, NextVisit ASC, c.Name ASC";

        command.CommandText = sql;
        command.With("$now", DateTime.Now.ToString("s"));
        if (!string.IsNullOrWhiteSpace(search)) command.With("$q", $"%{search.Trim()}%");

        return Read(command);
    }

    public ClientSummary? GetById(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = ClientSelect + " WHERE c.Id = $id GROUP BY c.Id";
        command.With("$id", id).With("$now", DateTime.Now.ToString("s"));
        return Read(command).FirstOrDefault();
    }

    /// <summary>
    /// Клиенты, которых пора звать снова: последний завершённый визит был больше
    /// <paramref name="weeks"/> недель назад, а новой записи нет.
    /// </summary>
    /// <remarks>
    /// Недавно приглашённые отсеиваются (<paramref name="cooldownDays"/>): иначе один и тот же
    /// человек попадал бы в список каждый день, и приглашения превратились бы в спам.
    /// </remarks>
    public List<ClientSummary> GetDueForVisit(int weeks, int cooldownDays = 14)
    {
        var threshold = DateTime.Now.AddDays(-7 * Math.Max(1, weeks));
        var cooldown = DateTime.Now.AddDays(-Math.Max(1, cooldownDays));

        return GetAll()
            .Where(c => c.LastVisit is not null
                        && c.LastVisit < threshold
                        && c.NextVisit is null
                        && (c.LastRemindedAt is null || c.LastRemindedAt < cooldown))
            .OrderBy(c => c.LastVisit)
            .ToList();
    }

    /// <summary>Отмечает, что клиента только что позвали вернуться.</summary>
    public void SetLastReminded(int clientId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Clients SET LastRemindedAt = $at WHERE Id = $id";
        command.With("$at", DateTime.Now.ToString("s")).With("$id", clientId);
        command.ExecuteNonQuery();
    }

    public void SetNotes(int clientId, string? notes)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Clients SET Notes = $notes WHERE Id = $id";
        command.With("$notes", string.IsNullOrWhiteSpace(notes) ? null : notes.Trim())
               .With("$id", clientId);
        command.ExecuteNonQuery();
    }

    /// <summary>Все записи клиента — для карточки в админке.</summary>
    public List<Booking> GetBookings(int clientId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
            WHERE b.ClientId = $id
            ORDER BY b.StartAt DESC
            """;
        command.With("$id", clientId);

        var result = new List<Booking>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Booking
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

        return result;
    }

    /// <summary>Сколько всего клиентов — для дашборда.</summary>
    public int Count()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Clients";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static List<ClientSummary> Read(SqliteCommand command)
    {
        var result = new List<ClientSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ClientSummary
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Phone = reader.Str(2),
                TelegramChatId = reader.StrOrNull(3),
                Notes = reader.StrOrNull(4),
                CreatedAt = reader.Str(5),
                LastRemindedAt = ParseDate(reader.StrOrNull(6)),
                Visits = reader.Int(7),
                LastVisit = ParseDate(reader.StrOrNull(8)),
                TotalSpent = reader.Decimal(9),
                NextVisit = ParseDate(reader.StrOrNull(10)),
                TelegramNick = reader.StrOrNull(11)
            });
        }

        return result;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;
}

/// <summary>Клиент со сводкой по визитам.</summary>
public sealed class ClientSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? TelegramChatId { get; set; }

    /// <summary>Ник в Telegram в виде <c>@nick</c>, если клиент его указывал при записи.</summary>
    public string? TelegramNick { get; set; }
    public string? Notes { get; set; }
    public string CreatedAt { get; set; } = "";

    public int Visits { get; set; }
    public DateTime? LastVisit { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? NextVisit { get; set; }

    /// <summary>Когда клиента в последний раз приглашали вернуться.</summary>
    public DateTime? LastRemindedAt { get; set; }

    public bool TelegramConnected => !string.IsNullOrWhiteSpace(TelegramChatId);

    /// <summary>Сколько дней прошло с последнего визита. null — визитов ещё не было.</summary>
    public int? DaysSinceVisit =>
        LastVisit is null ? null : (int)(DateTime.Now - LastVisit.Value).TotalDays;

    /// <summary>Давность последнего визита словами: «3 недели назад».</summary>
    public string SinceVisitText
    {
        get
        {
            if (DaysSinceVisit is not { } days) return "ещё не была";
            if (days < 1) return "сегодня";
            if (days < 14) return $"{days} {Money.Days(days)} назад";

            var weeks = days / 7;
            return $"{weeks} {Money.Weeks(weeks)} назад";
        }
    }

    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant()
            };
        }
    }
}
