using ELORA.Web.Models;

namespace ELORA.Web.Data.Repositories;

public sealed class ScheduleRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ScheduleRepository(SqliteConnectionFactory factory) => _factory = factory;

    public List<WorkingHours> GetWorkingHours(int? masterId = null)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, MasterId, DayOfWeek, StartTime, EndTime, IsActive FROM WorkingHours" +
            (masterId.HasValue ? " WHERE MasterId = $m" : "") +
            " ORDER BY MasterId, DayOfWeek";
        if (masterId.HasValue) command.With("$m", masterId.Value);

        var list = new List<WorkingHours>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new WorkingHours
            {
                Id = reader.Int(0),
                MasterId = reader.Int(1),
                DayOfWeek = reader.Int(2),
                StartTime = reader.Str(3),
                EndTime = reader.Str(4),
                IsActive = reader.Bool(5)
            });
        }
        return list;
    }

    public WorkingHours? GetWorkingHours(int masterId, DayOfWeek day)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, MasterId, DayOfWeek, StartTime, EndTime, IsActive FROM WorkingHours " +
            "WHERE MasterId = $m AND DayOfWeek = $d AND IsActive = 1 LIMIT 1";
        command.With("$m", masterId).With("$d", (int)day);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new WorkingHours
        {
            Id = reader.Int(0),
            MasterId = reader.Int(1),
            DayOfWeek = reader.Int(2),
            StartTime = reader.Str(3),
            EndTime = reader.Str(4),
            IsActive = reader.Bool(5)
        };
    }

    /// <summary>Полностью заменяет недельный график мастера.</summary>
    public void SaveWorkingHours(int masterId, IEnumerable<WorkingHours> hours)
    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM WorkingHours WHERE MasterId = $m";
            clear.With("$m", masterId);
            clear.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            "INSERT INTO WorkingHours (MasterId, DayOfWeek, StartTime, EndTime, IsActive) " +
            "VALUES ($m, $d, $s, $e, $a)";
        var pm = insert.Parameters.Add("$m", Microsoft.Data.Sqlite.SqliteType.Integer);
        var pd = insert.Parameters.Add("$d", Microsoft.Data.Sqlite.SqliteType.Integer);
        var ps = insert.Parameters.Add("$s", Microsoft.Data.Sqlite.SqliteType.Text);
        var pe = insert.Parameters.Add("$e", Microsoft.Data.Sqlite.SqliteType.Text);
        var pa = insert.Parameters.Add("$a", Microsoft.Data.Sqlite.SqliteType.Integer);

        foreach (var hour in hours)
        {
            pm.Value = masterId;
            pd.Value = hour.DayOfWeek;
            ps.Value = hour.StartTime;
            pe.Value = hour.EndTime;
            pa.Value = hour.IsActive ? 1 : 0;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<BlockedDate> GetBlockedDates()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT b.Id, b.MasterId, b.Date, b.Reason, m.Name FROM BlockedDates b " +
            "LEFT JOIN Masters m ON m.Id = b.MasterId ORDER BY b.Date DESC";
        var list = new List<BlockedDate>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new BlockedDate
            {
                Id = reader.Int(0),
                MasterId = reader.IntOrNull(1),
                Date = reader.Str(2),
                Reason = reader.StrOrNull(3)
            });
        }
        return list;
    }

    /// <summary>Дата заблокирована либо для конкретного мастера, либо для всех (MasterId IS NULL).</summary>
    public bool IsDateBlocked(int masterId, DateOnly date)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM BlockedDates WHERE Date = $d AND (MasterId IS NULL OR MasterId = $m)";
        command.With("$d", date.ToString("yyyy-MM-dd")).With("$m", masterId);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public void AddBlockedDate(int? masterId, DateOnly date, string? reason)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO BlockedDates (MasterId, Date, Reason) VALUES ($m, $d, $r)";
        command.With("$m", masterId).With("$d", date.ToString("yyyy-MM-dd")).With("$r", reason);
        command.ExecuteNonQuery();
    }

    public void RemoveBlockedDate(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM BlockedDates WHERE Id = $id";
        command.With("$id", id);
        command.ExecuteNonQuery();
    }
}
