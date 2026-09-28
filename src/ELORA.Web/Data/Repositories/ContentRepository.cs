using ELORA.Web.Models;

namespace ELORA.Web.Data.Repositories;

/// <summary>Портфолио, отзывы и FAQ.</summary>
public sealed class ContentRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ContentRepository(SqliteConnectionFactory factory) => _factory = factory;

    // ---------- Works ----------

    private const string WorkSelect = """
        SELECT w.Id, w.Title, w.Category, w.ImagePath, w.Description, w.MasterId, m.Name, w.SortOrder, w.IsActive
        FROM Works w
        LEFT JOIN Masters m ON m.Id = w.MasterId
        """;

    public List<Work> GetWorks(string? category = null, bool onlyActive = true, int? limit = null)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        var sql = WorkSelect + " WHERE 1 = 1";
        if (onlyActive) sql += " AND w.IsActive = 1";
        if (!string.IsNullOrWhiteSpace(category) && WorkCategories.IsKnown(category))
        {
            sql += " AND w.Category = $cat";
            command.With("$cat", category);
        }
        sql += " ORDER BY w.SortOrder, w.Id";
        if (limit.HasValue) sql += $" LIMIT {limit.Value}";
        command.CommandText = sql;
        return ReadWorks(command);
    }

    public Dictionary<string, int> CountWorksByCategory(bool onlyActive = true)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Category, COUNT(*) FROM Works" +
                              (onlyActive ? " WHERE IsActive = 1" : "") + " GROUP BY Category";
        var result = new Dictionary<string, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    public Work? GetWork(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = WorkSelect + " WHERE w.Id = $id";
        command.With("$id", id);
        return ReadWorks(command).FirstOrDefault();
    }

    public int SaveWork(Work work)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        if (work.Id == 0)
        {
            command.CommandText = """
                INSERT INTO Works (Title, Category, ImagePath, Description, MasterId, SortOrder, IsActive)
                VALUES ($title, $cat, $img, $desc, $master, $sort, $active);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE Works SET Title = $title, Category = $cat, ImagePath = $img, Description = $desc,
                    MasterId = $master, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", work.Id);
        }

        command.With("$title", work.Title).With("$cat", work.Category).With("$img", work.ImagePath)
               .With("$desc", work.Description).With("$master", work.MasterId)
               .With("$sort", work.SortOrder).With("$active", work.IsActive ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void DeleteWork(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Works WHERE Id = $id";
        command.With("$id", id);
        command.ExecuteNonQuery();
    }

    private static List<Work> ReadWorks(Microsoft.Data.Sqlite.SqliteCommand command)
    {
        var list = new List<Work>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Work
            {
                Id = reader.Int(0),
                Title = reader.Str(1),
                Category = reader.Str(2),
                ImagePath = reader.Str(3),
                Description = reader.StrOrNull(4),
                MasterId = reader.IntOrNull(5),
                MasterName = reader.StrOrNull(6),
                SortOrder = reader.Int(7),
                IsActive = reader.Bool(8)
            });
        }
        return list;
    }

    // ---------- Reviews ----------

    public List<Review> GetReviews(bool onlyActive = true, int? limit = null)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        var sql = "SELECT Id, ClientName, AvatarPath, Rating, Text, CreatedAt, SortOrder, IsActive FROM Reviews" +
                  (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Id";
        if (limit.HasValue) sql += $" LIMIT {limit.Value}";
        command.CommandText = sql;

        var list = new List<Review>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Review
            {
                Id = reader.Int(0),
                ClientName = reader.Str(1),
                AvatarPath = reader.StrOrNull(2),
                Rating = reader.Int(3),
                Text = reader.Str(4),
                CreatedAt = reader.Str(5),
                SortOrder = reader.Int(6),
                IsActive = reader.Bool(7)
            });
        }
        return list;
    }

    public Review? GetReview(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ClientName, AvatarPath, Rating, Text, CreatedAt, SortOrder, IsActive FROM Reviews WHERE Id = $id";
        command.With("$id", id);
        var list = new List<Review>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Review
            {
                Id = reader.Int(0),
                ClientName = reader.Str(1),
                AvatarPath = reader.StrOrNull(2),
                Rating = reader.Int(3),
                Text = reader.Str(4),
                CreatedAt = reader.Str(5),
                SortOrder = reader.Int(6),
                IsActive = reader.Bool(7)
            });
        }
        return list.FirstOrDefault();
    }

    public int SaveReview(Review review)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        if (review.Id == 0)
        {
            command.CommandText = """
                INSERT INTO Reviews (ClientName, AvatarPath, Rating, Text, CreatedAt, SortOrder, IsActive)
                VALUES ($name, $avatar, $rating, $text, $now, $sort, $active);
                SELECT last_insert_rowid();
                """;
            command.With("$now", string.IsNullOrWhiteSpace(review.CreatedAt) ? DateTime.Now.ToString("s") : review.CreatedAt);
        }
        else
        {
            command.CommandText = """
                UPDATE Reviews SET ClientName = $name, AvatarPath = $avatar, Rating = $rating,
                    Text = $text, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", review.Id);
        }

        command.With("$name", review.ClientName).With("$avatar", review.AvatarPath)
               .With("$rating", Math.Clamp(review.Rating, 1, 5)).With("$text", review.Text)
               .With("$sort", review.SortOrder).With("$active", review.IsActive ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void DeleteReview(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Reviews WHERE Id = $id";
        command.With("$id", id);
        command.ExecuteNonQuery();
    }

    // ---------- FAQ ----------

    public List<FaqItem> GetFaq(bool onlyActive = true)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Question, Answer, SortOrder, IsActive FROM FAQ" +
                              (onlyActive ? " WHERE IsActive = 1" : "") + " ORDER BY SortOrder, Id";
        var list = new List<FaqItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new FaqItem
            {
                Id = reader.Int(0),
                Question = reader.Str(1),
                Answer = reader.Str(2),
                SortOrder = reader.Int(3),
                IsActive = reader.Bool(4)
            });
        }
        return list;
    }

    public FaqItem? GetFaqItem(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Question, Answer, SortOrder, IsActive FROM FAQ WHERE Id = $id";
        command.With("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new FaqItem
        {
            Id = reader.Int(0),
            Question = reader.Str(1),
            Answer = reader.Str(2),
            SortOrder = reader.Int(3),
            IsActive = reader.Bool(4)
        };
    }

    public int SaveFaq(FaqItem item)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        if (item.Id == 0)
        {
            command.CommandText = """
                INSERT INTO FAQ (Question, Answer, SortOrder, IsActive)
                VALUES ($q, $a, $sort, $active);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE FAQ SET Question = $q, Answer = $a, SortOrder = $sort, IsActive = $active
                WHERE Id = $id;
                SELECT $id;
                """;
            command.With("$id", item.Id);
        }

        command.With("$q", item.Question).With("$a", item.Answer)
               .With("$sort", item.SortOrder).With("$active", item.IsActive ? 1 : 0);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void DeleteFaq(int id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FAQ WHERE Id = $id";
        command.With("$id", id);
        command.ExecuteNonQuery();
    }
}
