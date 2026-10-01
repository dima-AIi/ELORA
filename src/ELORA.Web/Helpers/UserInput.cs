namespace ELORA.Web.Helpers;

/// <summary>
/// Проверка данных, которые вводит человек, до того как они попадут в базу.
/// </summary>
/// <remarks>
/// <para>
/// Каждое поле проверяется по трём шагам: сначала вид и разрядность символов,
/// затем длина, затем содержание. Порядок неслучаен — он выбран так, чтобы
/// сообщение подсказывало, что именно исправить.
/// </para>
/// <para>
/// Правило одинаковое для всех полей: если человек ввёл мусор, приложение обязано
/// сказать об этом словами, а не молча отбросить введённое или сохранить его как
/// есть. Раньше, например, ник в Telegram без знака «@» просто исчезал из карточки
/// клиента, и владелец не мог понять, почему написать человеку не получится.
/// </para>
/// <para>
/// Проверки живут в одном классе, чтобы правило «что считается допустимым»
/// менялось в одном месте, а не по одному файлу за раз.
/// </para>
/// </remarks>
public static class UserInput
{
    /// <summary>Допустимая длина пароля.</summary>
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 100;

    /// <summary>Допустимая длина имени клиента.</summary>
    public const int NameMinLength = 2;
    public const int NameMaxLength = 50;

    /// <summary>Допустимое количество цифр в телефоне.</summary>
    public const int PhoneMinDigits = 10;
    public const int PhoneMaxDigits = 15;

    /// <summary>Максимальная длина комментария мастеру.</summary>
    public const int CommentMaxLength = 500;

    /// <summary>
    /// Проверка пароля администратора: не короче 8 символов, есть латинская
    /// буква, нет кириллицы.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Запрет кириллицы объясняется просто: эти буквы выглядят как английские,
    /// но набираются по другой раскладке. Человек, привыкший печатать на русской
    /// раскладке, напечатает «Пароль» вместо «Password», решит, что задал пароль,
    /// и потом не сможет войти. Отказ здесь понятнее, чем загадочная неудача.
    /// </para>
    /// <para>
    /// Требование латинской буквы не даёт поставить пароль из одних цифр или
    /// знаков: такой пароль формально длинный, но подбирается простым перебором.
    /// </para>
    /// </remarks>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "Введите пароль";

        if (password.Length < PasswordMinLength)
            return $"Пароль должен содержать не менее {PasswordMinLength} символов";

        if (password.Length > PasswordMaxLength)
            return $"Пароль не должен быть длиннее {PasswordMaxLength} символов";

        if (password.Any(IsCyrillic))
            return "Пароль не должен содержать кириллицу — используйте латинские буквы, например ChikoSlova2026";

        if (!password.Any(c => char.IsAsciiLetter(c)))
            return "Пароль должен содержать хотя бы одну латинскую букву";

        return null;
    }

    /// <summary>
    /// Проверка имени клиента: только буквы, пробел, дефис и апостроф.
    /// </summary>
    /// <remarks>
    /// Цифры запрещены намеренно. Имя читает мастер, и оно попадает в уведомление:
    /// «Анна2» там выглядит как опечатка. Заодно такая проверка отсекает попытки
    /// вставить в имя номер телефона или команду бота.
    /// </remarks>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Укажите имя";

        var text = name.Trim();

        if (text.Length < NameMinLength)
            return $"Имя должно содержать не менее {NameMinLength} символов";

        if (text.Length > NameMaxLength)
            return $"Имя не должно быть длиннее {NameMaxLength} символов";

        if (text.Any(char.IsDigit))
            return "Имя не должно содержать цифры";

        if (text.Any(c => !char.IsLetter(c) && c != ' ' && c != '-' && c != '\''))
            return "Имя может содержать только буквы, пробел и дефис";

        return null;
    }

    /// <summary>
    /// Проверка телефона: только цифры, пробел, плюс, скобки и дефисы.
    /// </summary>
    /// <remarks>
    /// Буквы запрещены. Раньше считалось только количество цифр, поэтому номер
    /// вида «+7-абв-99» проходил проверку и сохранялся в базу — владелец потом
    /// не мог ни позвонить клиенту, ни найти его в списке.
    /// </remarks>
    public static string? ValidatePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "Укажите телефон";

        var text = phone.Trim();

        if (text.Any(c => !char.IsDigit(c) && c is not (' ' or '+' or '-' or '(' or ')')))
            return "Укажите телефон, используя только цифры, пробел и знаки + - ( )";

        var digits = text.Count(char.IsDigit);
        if (digits < PhoneMinDigits)
            return $"Телефон должен содержать не менее {PhoneMinDigits} цифр";

        if (digits > PhoneMaxDigits)
            return $"Телефон не должен содержать более {PhoneMaxDigits} цифр";

        return null;
    }

    /// <summary>
    /// Проверка комментария мастеру: не более 500 символов.
/// <summary>
    /// Проверка ника в Telegram с объяснением причины отказа.
    /// </summary>
    /// <remarks>
    /// Сама нормализация живёт в <see cref="TelegramNick"/>: там решается, что
    /// считать ником. Здесь — только проверка с сообщением, потому что человек
    /// ввёл «анна» и не понял, почему поле осталось пустым.
    /// </remarks>
    public static string? ValidateTelegramNick(string? nick)
    {
        if (string.IsNullOrWhiteSpace(nick)) return null;

        var text = nick.Trim();

        // Ссылку t.me/anna принимаем как ник: люди присылают именно её.
        // Во всех остальных случаях знак «@» обязателен. Раньше проверка
        // молча принимала «anna» и превращала в «@anna», из-за чего человек
        // не понимал, что вводит: ник без собачки выглядит как готовый
        // результат, а на самом деле это обычный текст, который ещё не ник.
        if (!StartsWithLink(text) && !text.StartsWith('@'))
            return "Ник должен начинаться со знака @ — например @" + StripLinksAndAt(text);

        if (TelegramNick.Normalize(text) is not null) return null;

        var bare = StripLinksAndAt(text);

        if (bare.Length < 3)
            return "Ник слишком короткий: минимум 3 символа, например @anna";

        if (bare.Length > 32)
            return "Ник слишком длинный: максимум 32 символа";

        if (bare.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            return "Ник может содержать только латинские буквы, цифры и _";

        if (char.IsAsciiDigit(bare[0]) || bare[0] == '_')
            return "Ник не может начинаться с цифры или _";

        if (bare.Contains("__"))
            return "Ник не может содержать два подчёркивания подряд";

        return "Проверьте ник: он должен начинаться с @ и содержать 3–32 латинские буквы, цифры или _";
    }

    /// <summary>Ссылка ли это на профиль в Telegram.</summary>
    private static bool StartsWithLink(string text)
    {
        foreach (var prefix in new[]
        {
            "https://t.me/", "http://t.me/", "t.me/",
            "https://telegram.me/", "http://telegram.me/", "telegram.me/"
        })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// Проверка логина администратора: латиница, цифры, точка, дефис, подчёркивание.
    /// </summary>
    /// <remarks>
    /// Кириллица запрещена по той же причине, что и в пароле: русская «а» и
    /// английская «a» выглядят одинаково, но это разные символы — об этом узнаёшь
    /// не сразу, а при попытке войти.
    /// </remarks>
    public static string? ValidateLogin(string? login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return "Введите логин";

        var text = login.Trim();

        if (text.Length is < 3 or > 32)
            return "Логин должен содержать от 3 до 32 символов";

        if (text.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-'))
            return "Логин может содержать только латинские буквы, цифры, точку и _";

        return null;
    }

    /// <summary>Убирает префиксы ссылок и ведущий знак «@».</summary>
    private static string StripLinksAndAt(string text)
    {
        foreach (var prefix in new[]
        {
            "https://t.me/", "http://t.me/", "t.me/",
            "https://telegram.me/", "http://telegram.me/", "telegram.me/"
        })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
                break;
            }
        }

        var cut = text.IndexOfAny(new[] { '?', '/', ' ', '\t' });
        if (cut >= 0) text = text[..cut];

        return text.TrimStart('@').Trim();
    }

    /// <summary>Похоже ли содержимое на настоящий ник, если не учитывать ведущий «@».</summary>
    private static bool LooksLikeValidNick(string text) =>
        text.Length is >= 3 and <= 32 &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
        !char.IsAsciiDigit(text[0]) && text[0] != '_' &&
        !text.Contains("__") && !text.EndsWith('_');
        /// </summary>
        /// <remarks>
        /// Слишком длинный комментарий обрезать нельзя: клиент решит, что просьба
        /// записалась не полностью, и напишет заново. Лучше сказать об этом сразу.
        /// </remarks>
        public static string? ValidateComment(string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return null;

            if (comment.Trim().Length > CommentMaxLength)
                return $"Комментарий не должен быть длиннее {CommentMaxLength} символов";

            return null;
        }

        /// <summary>Кириллица: русские буквы, «ё» и украинские/белорусские знаки.</summary>
        public static bool IsCyrillic(char c) =>
            (c >= 'А' && c <= 'я') || (c >= 'Ё' && c <= 'ё') || (c >= 'Є' && c <= 'Ґ');
}
