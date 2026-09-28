namespace ELORA.Web.Data.Repositories;

public sealed class SettingsRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _loaded;

    public SettingsRepository(SqliteConnectionFactory factory) => _factory = factory;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            using var connection = _factory.Create();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Value FROM Settings";
            using var reader = command.ExecuteReader();
            while (reader.Read()) _cache[reader.GetString(0)] = reader.GetString(1);
            _loaded = true;
        }
    }

    public string Get(string key, string fallback = "")
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _cache.TryGetValue(key, out var value) ? value : fallback;
        }
    }

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;

    public int GetInt(string key, int fallback = 0) =>
        int.TryParse(Get(key), out var value) ? value : fallback;

    public void Set(string key, string value)
    {
        EnsureLoaded();
        using (var connection = _factory.Create())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($k, $v) " +
                                  "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
            command.With("$k", key).With("$v", value ?? "");
            command.ExecuteNonQuery();
        }
        lock (_lock)
        {
            _cache[key] = value ?? "";
        }
    }

    public Dictionary<string, string> GetAll()
    {
        EnsureLoaded();
        lock (_lock)
        {
            return new Dictionary<string, string>(_cache, StringComparer.OrdinalIgnoreCase);
        }
    }
}

public sealed class AdminUserRepository
{
    private readonly SqliteConnectionFactory _factory;

    public AdminUserRepository(SqliteConnectionFactory factory) => _factory = factory;

    public (int Id, string Login, string PasswordHash)? FindByLogin(string login)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Login, PasswordHash FROM AdminUsers WHERE Login = $login LIMIT 1";
        command.With("$login", login);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
    }

    public bool AnyExists()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AdminUsers";
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>Логин администратора — нужен аварийному сбросу пароля, когда логин не задан переменной.</summary>
    public string? FirstLogin()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Login FROM AdminUsers ORDER BY Id LIMIT 1";
        return command.ExecuteScalar() as string;
    }

    public void UpdatePassword(int id, string passwordHash)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AdminUsers SET PasswordHash = $hash WHERE Id = $id";
        command.With("$hash", passwordHash).With("$id", id);
        command.ExecuteNonQuery();
    }

    public void Create(string login, string passwordHash)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AdminUsers (Login, PasswordHash, CreatedAt) VALUES ($l, $h, $now)";
        command.With("$l", login).With("$h", passwordHash).With("$now", DateTime.Now.ToString("s"));
        command.ExecuteNonQuery();
    }
}
