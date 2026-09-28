using ELORA.Web.Models;

namespace ELORA.Web.Data.Repositories;

public sealed class MasterRepository
{
    private readonly SqliteConnectionFactory _factory;

    public MasterRepository(SqliteConnectionFactory factory) => _factory = factory;

    private const string Select = """
        SELECT Id, Name, Description, Phone, TelegramChatId, PhotoPath, Specialization, SortOrder, IsActive
        FROM Masters
        """;

    public List<Master> GetAll(bool onlyActive = true)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = Select + (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Id";
        var list = Read(command);
        foreach (var master in list) master.ServiceIds = GetServiceIds(master.Id);
        return list;
    }

    public Master? GetById(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = Select + " WHERE Id = $id";
        command.With("$id", id);
        var master = Read(command).FirstOrDefault();
        if (master is not null) master.ServiceIds = GetServiceIds(master.Id);
        return master;
    }

    /// <summary>Мастера, которые оказывают конкретную услугу.</summary>
    public List<Master> GetForService(int serviceId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = Select +
            " JOIN MasterServices ms ON ms.MasterId = Masters.Id" +
            " WHERE Masters.IsActive = 1 AND ms.ServiceId = $service" +
            " ORDER BY Masters.SortOrder, Masters.Id";
        command.With("$service", serviceId);
        return Read(command);
    }

    public List<int> GetServiceIds(int masterId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceId FROM MasterServices WHERE MasterId = $id";
        command.With("$id", masterId);
        var ids = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    public int Save(Master master)
    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (master.Id == 0)
        {
            command.CommandText = """
                INSERT INTO Masters (Name, Description, Phone, TelegramChatId, PhotoPath, Specialization, SortOrder, IsActive)
                VALUES ($name, $desc, $phone, $tg, $photo, $spec, $sort, $active);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE Masters SET Name = $name, Description = $desc, Phone = $phone, TelegramChatId = $tg,
                    PhotoPath = $photo, Specialization = $spec, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", master.Id);
        }

        command.With("$name", master.Name)
               .With("$desc", master.Description)
               .With("$phone", master.Phone)
               .With("$tg", master.TelegramChatId)
               .With("$photo", master.PhotoPath)
               .With("$spec", master.Specialization)
               .With("$sort", master.SortOrder)
               .With("$active", master.IsActive ? 1 : 0);

        var id = Convert.ToInt32(command.ExecuteScalar());

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM MasterServices WHERE MasterId = $id";
            clear.With("$id", id);
            clear.ExecuteNonQuery();
        }

        if (master.ServiceIds.Count > 0)
        {
            using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT OR IGNORE INTO MasterServices (MasterId, ServiceId) VALUES ($m, $s)";
            var pm = link.Parameters.Add("$m", Microsoft.Data.Sqlite.SqliteType.Integer);
            var ps = link.Parameters.Add("$s", Microsoft.Data.Sqlite.SqliteType.Integer);
            foreach (var serviceId in master.ServiceIds.Distinct())
            {
                pm.Value = id;
                ps.Value = serviceId;
                link.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return id;
    }

    /// <summary>Сколько записей у каждого мастера — админка прячет «Удалить» там, где сервер откажет.</summary>
    public Dictionary<int, int> GetBookingCounts()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MasterId, COUNT(*) FROM Bookings GROUP BY MasterId";

        var counts = new Dictionary<int, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) counts[reader.GetInt32(0)] = reader.GetInt32(1);
        return counts;
    }

    /// <summary>
    /// Удалить мастера совсем. Возвращает <c>false</c>, если у него есть записи: они ссылаются
    /// на мастера жёстко, и терять историю клиентов нельзя — такого мастера только скрывают.
    /// Услуги, часы работы и блокировки уходят каскадом, работы остаются без автора
    /// (<c>Works.MasterId → NULL</c>).
    /// </summary>
    public bool Delete(int masterId)
    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM Bookings WHERE MasterId = $id";
            check.With("$id", masterId);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return false;
        }

        int affected;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Masters WHERE Id = $id";
            command.With("$id", masterId);
            affected = command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected > 0;
    }

    public void SetActive(int id, bool active)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Masters SET IsActive = $active WHERE Id = $id";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetTelegramChatId(int id, string? chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Masters SET TelegramChatId = $chat WHERE Id = $id";
        command.With("$chat", chatId).With("$id", id);
        command.ExecuteNonQuery();
    }

    private static List<Master> Read(Microsoft.Data.Sqlite.SqliteCommand command)
    {
        var list = new List<Master>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Master
            {
                Id = reader.Int(0),
                Name = reader.Str(1),
                Description = reader.StrOrNull(2),
                Phone = reader.StrOrNull(3),
                TelegramChatId = reader.StrOrNull(4),
                PhotoPath = reader.StrOrNull(5),
                Specialization = reader.StrOrNull(6),
                SortOrder = reader.Int(7),
                IsActive = reader.Bool(8)
            });
        }
        return list;
    }
}
