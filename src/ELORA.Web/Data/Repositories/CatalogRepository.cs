using ELORA.Web.Models;

namespace ELORA.Web.Data.Repositories;

public sealed class CatalogRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CatalogRepository(SqliteConnectionFactory factory) => _factory = factory;

    private const string ServiceSelect = """
        SELECT s.Id, s.CategoryId, c.Slug, c.Name, s.Name, s.Description,
               s.DurationMinutes, s.Price, s.SortOrder, s.IsActive
        FROM Services s
        JOIN ServiceCategories c ON c.Id = s.CategoryId
        """;

    public List<ServiceCategory> GetCategories(bool onlyActive = true)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Slug, Name, Description, ImagePath, PriceFrom, SortOrder, IsActive FROM ServiceCategories" +
            (onlyActive ? " WHERE IsActive = 1" : "") +
            " ORDER BY SortOrder, Id";

        var list = new List<ServiceCategory>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ServiceCategory
            {
                Id = reader.Int(0),
                Slug = reader.Str(1),
                Name = reader.Str(2),
                Description = reader.StrOrNull(3),
                ImagePath = reader.StrOrNull(4),
                PriceFrom = reader.Decimal(5),
                SortOrder = reader.Int(6),
                IsActive = reader.Bool(7)
            });
        }
        return list;
    }

    public ServiceCategory? GetCategory(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Slug, Name, Description, ImagePath, PriceFrom, SortOrder, IsActive FROM ServiceCategories WHERE Id = $id";
        command.With("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new ServiceCategory
        {
            Id = reader.Int(0),
            Slug = reader.Str(1),
            Name = reader.Str(2),
            Description = reader.StrOrNull(3),
            ImagePath = reader.StrOrNull(4),
            PriceFrom = reader.Decimal(5),
            SortOrder = reader.Int(6),
            IsActive = reader.Bool(7)
        };
    }

    public List<Service> GetServices(bool onlyActive = true)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = ServiceSelect + (onlyActive ? " WHERE s.IsActive = 1 AND c.IsActive = 1" : "") +
                              " ORDER BY c.SortOrder, s.SortOrder, s.Id";
        return ReadServices(command);
    }

    public Service? GetService(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = ServiceSelect + " WHERE s.Id = $id";
        command.With("$id", id);
        return ReadServices(command).FirstOrDefault();
    }

    public List<Service> GetServicesForMaster(int masterId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = ServiceSelect +
            " JOIN MasterServices ms ON ms.ServiceId = s.Id" +
            " WHERE ms.MasterId = $master AND s.IsActive = 1 AND c.IsActive = 1" +
            " ORDER BY c.SortOrder, s.SortOrder";
        command.With("$master", masterId);
        return ReadServices(command);
    }

    public int SaveCategory(ServiceCategory category)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        if (category.Id == 0)
        {
            command.CommandText = """
                INSERT INTO ServiceCategories (Slug, Name, Description, ImagePath, PriceFrom, SortOrder, IsActive)
                VALUES ($slug, $name, $desc, $img, $from, $sort, $active);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE ServiceCategories SET Slug = $slug, Name = $name, Description = $desc,
                    ImagePath = $img, PriceFrom = $from, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", category.Id);
        }

        command.With("$slug", category.Slug)
               .With("$name", category.Name)
               .With("$desc", category.Description)
               .With("$img", category.ImagePath)
               .With("$from", (double)category.PriceFrom)
               .With("$sort", category.SortOrder)
               .With("$active", category.IsActive ? 1 : 0);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int SaveService(Service service)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        if (service.Id == 0)
        {
            command.CommandText = """
                INSERT INTO Services (CategoryId, Name, Description, DurationMinutes, Price, SortOrder, IsActive)
                VALUES ($cat, $name, $desc, $dur, $price, $sort, $active);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE Services SET CategoryId = $cat, Name = $name, Description = $desc,
                    DurationMinutes = $dur, Price = $price, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", service.Id);
        }

        command.With("$cat", service.CategoryId)
               .With("$name", service.Name)
               .With("$desc", service.Description)
               .With("$dur", service.DurationMinutes)
               .With("$price", (double)service.Price)
               .With("$sort", service.SortOrder)
               .With("$active", service.IsActive ? 1 : 0);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Услуги не удаляются физически, если на них есть записи — вместо этого деактивируются.</summary>
    public void SetServiceActive(int id, bool active)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Services SET IsActive = $active WHERE Id = $id";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Сколько записей сделано на каждую услугу — по ним админка решает, что можно удалять.</summary>
    public Dictionary<int, int> GetBookingCounts()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceId, COUNT(*) FROM Bookings GROUP BY ServiceId";

        var counts = new Dictionary<int, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) counts[reader.GetInt32(0)] = reader.GetInt32(1);
        return counts;
    }

    /// <summary>
    /// Удалить услугу совсем. Возвращает <c>false</c>, если на неё есть записи: в них лежит
    /// история клиентов, и такая услуга только скрывается. Связи с мастерами уходят каскадом.
    /// </summary>
    public bool DeleteService(int serviceId)
    {
        using var connection = _factory.Create();
        using var transaction = connection.BeginTransaction();

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM Bookings WHERE ServiceId = $id";
            check.With("$id", serviceId);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return false;
        }

        int affected;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Services WHERE Id = $id";
            command.With("$id", serviceId);
            affected = command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected > 0;
    }

    public void SetCategoryActive(int id, bool active)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ServiceCategories SET IsActive = $active WHERE Id = $id";
        command.With("$active", active ? 1 : 0).With("$id", id);
        command.ExecuteNonQuery();
    }

    private static List<Service> ReadServices(Microsoft.Data.Sqlite.SqliteCommand command)
    {
        var list = new List<Service>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Service
            {
                Id = reader.Int(0),
                CategoryId = reader.Int(1),
                CategorySlug = reader.Str(2),
                CategoryName = reader.Str(3),
                Name = reader.Str(4),
                Description = reader.StrOrNull(5),
                DurationMinutes = reader.Int(6),
                Price = reader.Decimal(7),
                SortOrder = reader.Int(8),
                IsActive = reader.Bool(9)
            });
        }
        return list;
    }
}
