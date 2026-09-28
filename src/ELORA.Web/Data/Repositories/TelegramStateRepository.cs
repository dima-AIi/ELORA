using System.Text.Json;
using ELORA.Web.Models;
using ELORA.Web.Services.Telegram;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data.Repositories;

/// <summary>
/// Состояние шага диалога в боте. Одна запись на пару «чат + роль»: у одного человека
/// диалог с клиентским ботом и с админским ведётся независимо.
/// </summary>
public sealed class TelegramStateRepository
{
    private readonly SqliteConnectionFactory _factory;

    public TelegramStateRepository(SqliteConnectionFactory factory) => _factory = factory;

    public TelegramState? Get(TelegramRole role, long chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Step, BookingId, MasterId, Payload FROM TelegramStates WHERE ChatId = $chat AND Role = $role";
        command.With("$chat", chatId.ToString()).With("$role", RoleName(role));

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new TelegramState
        {
            ChatId = chatId,
            Step = reader.Str(0),
            BookingId = reader.IntOrNull(1),
            MasterId = reader.IntOrNull(2),
            Data = ParsePayload(reader.StrOrNull(3))
        };
    }

    /// <summary>Сохраняет состояние шага. Перезаписывает предыдущее состояние этого чата.</summary>
    public void Save(TelegramRole role, TelegramState state)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TelegramStates (ChatId, Role, Step, BookingId, MasterId, Payload, UpdatedAt)
            VALUES ($chat, $role, $step, $booking, $master, $payload, $now)
            ON CONFLICT(ChatId, Role) DO UPDATE SET
                Step      = excluded.Step,
                BookingId = excluded.BookingId,
                MasterId  = excluded.MasterId,
                Payload   = excluded.Payload,
                UpdatedAt = excluded.UpdatedAt
            """;

        command.With("$chat", state.ChatId.ToString())
               .With("$role", RoleName(role))
               .With("$step", state.Step)
               .With("$booking", state.BookingId)
               .With("$master", state.MasterId)
               .With("$payload", JsonSerializer.Serialize(state.Data))
               .With("$now", DateTime.Now.ToString("s"));

        command.ExecuteNonQuery();
    }

    /// <summary>Сбрасывает диалог: шаг завершён или отменён.</summary>
    public void Clear(TelegramRole role, long chatId)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TelegramStates WHERE ChatId = $chat AND Role = $role";
        command.With("$chat", chatId.ToString()).With("$role", RoleName(role));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Убирает брошенные диалоги. Без этого база копит состояния тех, кто начал перенос
    /// и закрыл Telegram.
    /// </summary>
    public int PurgeOlderThan(TimeSpan age)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TelegramStates WHERE UpdatedAt < $before";
        command.With("$before", DateTime.Now.Subtract(age).ToString("s"));
        return command.ExecuteNonQuery();
    }

    public static string RoleName(TelegramRole role) =>
        role == TelegramRole.Admin ? "admin" : "client";

    private static Dictionary<string, string> ParsePayload(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (parsed is null) return result;
            foreach (var pair in parsed)
                if (!string.IsNullOrWhiteSpace(pair.Value)) result[pair.Key] = pair.Value;
        }
        catch (JsonException)
        {
            // Битый payload — не повод падать: просто начинаем шаг заново.
        }

        return result;
    }
}
