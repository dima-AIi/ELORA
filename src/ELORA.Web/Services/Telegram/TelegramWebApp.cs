using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ELORA.Web.Services.Telegram;

/// <summary>Кто пришёл из Mini App: данные пользователя, которые Telegram положил в подписанную строку.</summary>
public sealed record TelegramWebAppUser(long Id, string Name, string? Username);

/// <summary>
/// Проверка <c>initData</c> — строки, которую Telegram передаёт странице, открытой как Mini App.
/// </summary>
/// <remarks>
/// Подпись считается не «токеном в чистом виде», а производным ключом:
/// <c>secret = HMAC_SHA256("WebAppData", bot_token)</c>, затем <c>HMAC_SHA256(secret, data_check_string)</c>.
/// Токен бота в браузер не попадает никогда — страница присылает только подписанные данные,
/// а сервер сверяет их сам. Это и есть вход без пароля: подделать строку, не зная токена, нельзя.
/// </remarks>
public static class TelegramWebApp
{
    private const string HashField = "hash";

    /// <summary>
    /// Проверяет подпись и свежесть данных. Возвращает <c>false</c> и причину словами,
    /// если строку подделали, она устарела или в ней нет пользователя.
    /// </summary>
    public static bool TryValidate(
        string? initData,
        string? botToken,
        TimeSpan maxAge,
        out TelegramWebAppUser? user,
        out string? error)
    {
        user = null;
        error = null;

        if (string.IsNullOrWhiteSpace(initData))
        {
            error = "Telegram не передал данные для входа";
            return false;
        }

        if (string.IsNullOrWhiteSpace(botToken))
        {
            error = "не задан токен админского бота";
            return false;
        }

        var fields = Parse(initData);

        if (!fields.TryGetValue(HashField, out var hash) || string.IsNullOrWhiteSpace(hash))
        {
            error = "в присланных данных нет подписи";
            return false;
        }

        // data_check_string — все поля кроме hash, по алфавиту, через перевод строки.
        // Порядок именно такой: так его считает Telegram, иначе подпись не сойдётся.
        var checkString = string.Join('\n', fields
            .Where(pair => pair.Key != HashField)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));

        if (!SignatureMatches(ComputeHash(checkString, botToken!), hash))
        {
            error = "подпись не совпадает";
            return false;
        }

        if (!fields.TryGetValue("auth_date", out var rawDate) ||
            !long.TryParse(rawDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var authDate))
        {
            error = "в присланных данных нет времени входа";
            return false;
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authDate);
        if (age > maxAge)
        {
            error = "данные входа устарели, откройте панель заново";
            return false;
        }

        if (!fields.TryGetValue("user", out var userJson) || string.IsNullOrWhiteSpace(userJson))
        {
            error = "Telegram не передал пользователя";
            return false;
        }

        return TryReadUser(userJson, out user, out error);
    }

    /// <summary>Разбирает строку запроса в поля. Значения декодируются — подпись считается по декодированным.</summary>
    private static Dictionary<string, string> Parse(string initData)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var chunk in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = chunk.IndexOf('=');
            if (separator <= 0) continue;

            var key = WebUtility.UrlDecode(chunk[..separator]);
            var value = WebUtility.UrlDecode(chunk[(separator + 1)..]);
            if (!string.IsNullOrEmpty(key)) fields[key] = value;
        }

        return fields;
    }

    private static bool TryReadUser(string json, out TelegramWebAppUser? user, out string? error)
    {
        user = null;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
            {
                error = "в присланных данных нет идентификатора пользователя";
                return false;
            }

            var first = root.TryGetProperty("first_name", out var f) ? f.GetString() : null;
            var last = root.TryGetProperty("last_name", out var l) ? l.GetString() : null;
            var username = root.TryGetProperty("username", out var u) ? u.GetString() : null;

            var name = string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part)));
            if (string.IsNullOrWhiteSpace(name)) name = username ?? $"id{id}";

            user = new TelegramWebAppUser(id, name, username);
            return true;
        }
        catch (JsonException)
        {
            error = "не удалось разобрать данные пользователя";
            return false;
        }
    }

    private static string ComputeHash(string checkString, string botToken)
    {
        using var secretHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretHmac.ComputeHash(Encoding.UTF8.GetBytes(botToken));

        using var dataHmac = new HMACSHA256(secretKey);
        var hash = dataHmac.ComputeHash(Encoding.UTF8.GetBytes(checkString));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Сравнение за постоянное время: посимвольное сравнение выдаёт подпись по времени ответа.</summary>
    private static bool SignatureMatches(string computed, string received)
    {
        var a = Encoding.ASCII.GetBytes(computed.ToLowerInvariant());
        var b = Encoding.ASCII.GetBytes(received.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
