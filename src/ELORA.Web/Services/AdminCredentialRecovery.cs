using ELORA.Web.Data.Repositories;

namespace ELORA.Web.Services;

/// <summary>
/// Аварийный вход в админку, когда пароль потерян.
///
/// Пароль администратора лежит в базе хешем, прочитать его обратно нельзя, а форма смены
/// пароля требует текущий — то есть при утере войти нечем. Поэтому при старте сайта
/// проверяется переменная окружения: если она задана, пароль администратора ставится на её
/// значение. Задаёт её владелец в панели хостинга, а панель — то место, которое ему
/// подконтрольно и недоступно постороннему.
///
/// Кнопки «сбросить пароль, не зная текущего» в самой админке быть не должно: тогда
/// угнанная сессия закрепляется навсегда, а с требованием текущего пароля владелец хотя бы
/// может отобрать доступ, сменив пароль.
/// </summary>
public static class AdminCredentialRecovery
{
    public const string PasswordVariable = "ELORA_ADMIN_RESET_PASSWORD";
    public const string LoginVariable = "ELORA_ADMIN_RESET_LOGIN";

    private const int MinLength = 8;

    /// <summary>Задана ли аварийная переменная — админка показывает это предупреждением.</summary>
    public static bool IsArmed =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordVariable));

    /// <summary>Применить аварийный сброс. Возвращает true, если пароль был изменён.</summary>
    public static bool Apply(AdminUserRepository admins, ILogger logger)
    {
        var password = Environment.GetEnvironmentVariable(PasswordVariable);
        if (string.IsNullOrWhiteSpace(password)) return false;

        if (password.Trim().Length < MinLength)
        {
            logger.LogWarning(
                "{Variable}: пароль короче {Min} символов — сброс пропущен",
                PasswordVariable, MinLength);
            return false;
        }

        var login = Environment.GetEnvironmentVariable(LoginVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(login)) login = admins.FirstLogin();
        if (string.IsNullOrWhiteSpace(login))
        {
            logger.LogWarning(
                "{Variable}: в базе нет администратора — сброс пропущен", PasswordVariable);
            return false;
        }

        var existing = admins.FindByLogin(login);
        if (existing is null)
        {
            admins.Create(login, PasswordHasher.Hash(password));
            logger.LogWarning(
                "Аварийный сброс: создан администратор '{Login}' из переменной {Variable}. " +
                "Войдите и удалите переменную.",
                login, PasswordVariable);
            return true;
        }

        admins.UpdatePassword(existing.Value.Id, PasswordHasher.Hash(password));
        logger.LogWarning(
            "Аварийный сброс: пароль администратора '{Login}' заменён значением переменной {Variable}. " +
            "Пока переменная задана, смена пароля из админки не сохранится после перезапуска — удалите её.",
            login, PasswordVariable);
        return true;
    }
}
