using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data;

/// <summary>
/// Стартовое наполнение ELORA. Запускается только для пустых таблиц,
/// поэтому изменения, сделанные в админке, не затираются.
/// </summary>
public static class Seed
{
    public static void Run(SqliteConnection connection, ILogger logger)
    {
        SeedAdmin(connection, logger);
        SeedCatalog(connection);
        SeedMasters(connection);
        SeedSchedule(connection);
        SeedWorks(connection);
        SeedReviews(connection);
        SeedFaq(connection);
        SeedSettings(connection);
    }

    private static bool IsEmpty(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar()) == 0;
    }

    private static void SeedAdmin(SqliteConnection connection, ILogger logger)
    {
        if (!IsEmpty(connection, "AdminUsers")) return;

        var login = Environment.GetEnvironmentVariable("ELORA_ADMIN_LOGIN") ?? "admin";
        var password = Environment.GetEnvironmentVariable("ELORA_ADMIN_PASSWORD") ?? "elora2026";

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO AdminUsers (Login, PasswordHash, CreatedAt) VALUES ($login, $hash, $now)";
        command.Parameters.AddWithValue("$login", login);
        command.Parameters.AddWithValue("$hash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("s"));
        command.ExecuteNonQuery();

        logger.LogInformation(
            "Создан администратор '{Login}'. Смените пароль в админ-панели (раздел «Профиль»).", login);
    }

    private static void SeedCatalog(SqliteConnection connection)
    {
        if (IsEmpty(connection, "ServiceCategories"))
        {
            var categories = new (string Slug, string Name, string Description, string Image, decimal From, int Sort)[]
            {
                ("manicure", "Маникюр", "Аккуратная форма, ухоженные руки и стойкое покрытие.", "/images/services/manicure.jpg", 1000m, 1),
                ("pedicure", "Педикюр", "Уход за стопами, обработка и покрытие премиальными материалами.", "/images/services/pedicure.jpg", 1200m, 2),
                ("lashes", "Ресницы", "Наращивание и ламинирование с естественным эффектом.", "/images/services/lashes.jpg", 1500m, 3),
                ("brows", "Брови", "Коррекция, окрашивание и долговременная укладка.", "/images/services/brows.jpg", 1500m, 4),
                ("hair", "Волосы", "Стрижки, окрашивание и уход для здорового блеска.", "/images/services/hair.jpg", 1500m, 5),
                ("tattoo", "Тату", "Художественные татуировки и аккуратные мини-работы.", "/images/services/tattoo.jpg", 2000m, 6)
            };

            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO ServiceCategories (Slug, Name, Description, ImagePath, PriceFrom, SortOrder, IsActive) " +
                "VALUES ($slug, $name, $desc, $img, $from, $sort, 1)";
            var pSlug = insert.Parameters.Add("$slug", SqliteType.Text);
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            var pDesc = insert.Parameters.Add("$desc", SqliteType.Text);
            var pImg = insert.Parameters.Add("$img", SqliteType.Text);
            var pFrom = insert.Parameters.Add("$from", SqliteType.Real);
            var pSort = insert.Parameters.Add("$sort", SqliteType.Integer);

            foreach (var c in categories)
            {
                pSlug.Value = c.Slug;
                pName.Value = c.Name;
                pDesc.Value = c.Description;
                pImg.Value = c.Image;
                pFrom.Value = (double)c.From;
                pSort.Value = c.Sort;
                insert.ExecuteNonQuery();
            }
        }

        if (!IsEmpty(connection, "Services")) return;

        var categoryIds = new Dictionary<string, int>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT Id, Slug FROM ServiceCategories";
            using var reader = read.ExecuteReader();
            while (reader.Read()) categoryIds[reader.GetString(1)] = reader.GetInt32(0);
        }

        var services = new (string Cat, string Name, string Desc, int Duration, decimal Price)[]
        {
            ("manicure", "Классический маникюр", "Обработка кутикулы, придание формы, уход.", 60, 1000m),
            ("manicure", "Аппаратный маникюр", "Бережная обработка аппаратом без воды.", 75, 1300m),
            ("manicure", "Маникюр + гель-лак", "Стойкое покрытие до трёх недель.", 90, 1500m),
            ("manicure", "Наращивание ногтей", "Моделирование формы гелем, любой дизайн.", 150, 2500m),

            ("pedicure", "Классический педикюр", "Обработка стоп, кутикулы и ногтей.", 75, 1200m),
            ("pedicure", "Аппаратный педикюр", "Аппаратная обработка и уход за кожей стоп.", 90, 1600m),
            ("pedicure", "Педикюр + гель-лак", "Полная обработка со стойким покрытием.", 105, 1800m),
            ("pedicure", "Покрытие стоп", "Обновление покрытия без обработки.", 60, 2200m),

            ("lashes", "Ламинирование ресниц", "Изгиб, питание и насыщенный цвет.", 75, 1500m),
            ("lashes", "Наращивание ресниц", "Классика, 2D или 3D объём.", 120, 2000m),
            ("lashes", "Коррекция ресниц", "Обновление наращивания.", 60, 1000m),
            ("lashes", "Окрашивание ресниц", "Краска и уход для выразительного взгляда.", 45, 1200m),

            ("brows", "Коррекция бровей", "Форма по типу лица, работа пинцетом и воском.", 45, 1500m),
            ("brows", "Окрашивание бровей", "Краска или хна, подобранная под тип кожи.", 45, 1000m),
            ("brows", "Ламинирование бровей", "Долговременная укладка и питание.", 75, 2000m),
            ("brows", "Комплекс брови + ресницы", "Полный уход за взглядом за один визит.", 120, 2500m),

            ("hair", "Женская стрижка", "Стрижка с учётом структуры волос.", 90, 1500m),
            ("hair", "Окрашивание волос", "Однотонное окрашивание профессиональными красителями.", 150, 3500m),
            ("hair", "Укладка волос", "Локоны, объём или гладкая укладка.", 60, 1200m),
            ("hair", "Уход и восстановление", "Восстанавливающая процедура для волос.", 90, 2000m),

            ("tattoo", "Мини-тату", "Небольшая работа до 5 см.", 60, 2000m),
            ("tattoo", "Тату среднего размера", "Работа до 15 см, авторский эскиз.", 180, 5000m),
            ("tattoo", "Сеанс татуировки", "Большая работа, сеанс до 4 часов.", 240, 6000m),
            ("tattoo", "Коррекция татуировки", "Обновление или перекрытие старой работы.", 90, 2500m)
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Services (CategoryId, Name, Description, DurationMinutes, Price, SortOrder, IsActive) " +
            "VALUES ($cat, $name, $desc, $dur, $price, $sort, 1)";
        var cCat = cmd.Parameters.Add("$cat", SqliteType.Integer);
        var cName = cmd.Parameters.Add("$name", SqliteType.Text);
        var cDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
        var cDur = cmd.Parameters.Add("$dur", SqliteType.Integer);
        var cPrice = cmd.Parameters.Add("$price", SqliteType.Real);
        var cSort = cmd.Parameters.Add("$sort", SqliteType.Integer);

        var order = 0;
        foreach (var s in services)
        {
            cCat.Value = categoryIds[s.Cat];
            cName.Value = s.Name;
            cDesc.Value = s.Desc;
            cDur.Value = s.Duration;
            cPrice.Value = (double)s.Price;
            cSort.Value = ++order;
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedMasters(SqliteConnection connection)
    {
        if (IsEmpty(connection, "Masters"))
        {
            var masters = new (string Name, string Desc, string Phone, string Spec, string Photo, int Sort)[]
            {
                ("Анна Соколова", "Мастер ногтевого сервиса, 8 лет практики. Работает с любым дизайном.", "+7 (999) 123-45-67", "Маникюр · Педикюр", "/images/masters/anna.jpg", 1),
                ("Ирина Волкова", "Специалист по ресницам и бровям. Естественные и выразительные эффекты.", "+7 (999) 123-45-68", "Ресницы · Брови", "/images/masters/irina.jpg", 2),
                ("Мария Лебедева", "Стилист-парикмахер. Стрижки, окрашивание и восстановление волос.", "+7 (999) 123-45-69", "Волосы", "/images/masters/maria.jpg", 3),
                ("Даниил Орлов", "Тату-мастер. Минимализм, графика и авторские эскизы.", "+7 (999) 123-45-70", "Тату", "/images/masters/daniil.jpg", 4)
            };

            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO Masters (Name, Description, Phone, TelegramChatId, PhotoPath, Specialization, SortOrder, IsActive) " +
                "VALUES ($name, $desc, $phone, NULL, $photo, $spec, $sort, 1)";
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            var pDesc = insert.Parameters.Add("$desc", SqliteType.Text);
            var pPhone = insert.Parameters.Add("$phone", SqliteType.Text);
            var pPhoto = insert.Parameters.Add("$photo", SqliteType.Text);
            var pSpec = insert.Parameters.Add("$spec", SqliteType.Text);
            var pSort = insert.Parameters.Add("$sort", SqliteType.Integer);

            foreach (var m in masters)
            {
                pName.Value = m.Name;
                pDesc.Value = m.Desc;
                pPhone.Value = m.Phone;
                pPhoto.Value = m.Photo;
                pSpec.Value = m.Spec;
                pSort.Value = m.Sort;
                insert.ExecuteNonQuery();
            }
        }

        if (!IsEmpty(connection, "MasterServices")) return;

        // Распределение: мастер → категории услуг
        var map = new Dictionary<string, string[]>
        {
            ["Анна Соколова"] = new[] { "manicure", "pedicure" },
            ["Ирина Волкова"] = new[] { "lashes", "brows" },
            ["Мария Лебедева"] = new[] { "hair" },
            ["Даниил Орлов"] = new[] { "tattoo" }
        };

        using var link = connection.CreateCommand();
        link.CommandText =
            "INSERT OR IGNORE INTO MasterServices (MasterId, ServiceId) " +
            "SELECT $master, s.Id FROM Services s " +
            "JOIN ServiceCategories c ON c.Id = s.CategoryId WHERE c.Slug = $slug";
        var lMaster = link.Parameters.Add("$master", SqliteType.Integer);
        var lSlug = link.Parameters.Add("$slug", SqliteType.Text);

        foreach (var (masterName, slugs) in map)
        {
            using var find = connection.CreateCommand();
            find.CommandText = "SELECT Id FROM Masters WHERE Name = $name LIMIT 1";
            find.Parameters.AddWithValue("$name", masterName);
            var result = find.ExecuteScalar();
            if (result is not long masterId) continue;

            foreach (var slug in slugs)
            {
                lMaster.Value = masterId;
                lSlug.Value = slug;
                link.ExecuteNonQuery();
            }
        }
    }

    private static void SeedSchedule(SqliteConnection connection)
    {
        if (!IsEmpty(connection, "WorkingHours")) return;

        using var masters = connection.CreateCommand();
        masters.CommandText = "SELECT Id FROM Masters ORDER BY Id";
        var ids = new List<long>();
        using (var reader = masters.ExecuteReader())
        {
            while (reader.Read()) ids.Add(reader.GetInt64(0));
        }

        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO WorkingHours (MasterId, DayOfWeek, StartTime, EndTime, IsActive) " +
            "VALUES ($master, $day, $start, $end, 1)";
        var pMaster = insert.Parameters.Add("$master", SqliteType.Integer);
        var pDay = insert.Parameters.Add("$day", SqliteType.Integer);
        var pStart = insert.Parameters.Add("$start", SqliteType.Text);
        var pEnd = insert.Parameters.Add("$end", SqliteType.Text);

        foreach (var id in ids)
        {
            // Понедельник–суббота, воскресенье — выходной.
            for (var day = 1; day <= 6; day++)
            {
                pMaster.Value = id;
                pDay.Value = day;
                pStart.Value = "09:00";
                pEnd.Value = "19:00";
                insert.ExecuteNonQuery();
            }
        }
    }

    private static void SeedWorks(SqliteConnection connection)
    {
        if (!IsEmpty(connection, "Works")) return;

        // Фотографии взяты из утверждённого макета (ELORA_assets/ELORA_separated_photos).
        // Порядок перемешан по направлениям: в галерее соседние карточки — из разных услуг,
        // как в референсе, а не шесть маникюров подряд.
        var works = new (string Category, string Image, string Title, string Desc)[]
        {
            (WorkCategories.Manicure, "manicure-01", "Нюдовый маникюр", "Гель-лак, мягкая форма"),
            (WorkCategories.Lashes,   "lashes-01",   "Классическое наращивание", "Натуральный изгиб"),
            (WorkCategories.Hair,     "hair-02",     "Собранная причёска", "Вечерний образ"),
            (WorkCategories.Brows,    "brows-01",    "Коррекция формы", "Мягкая линия"),
            (WorkCategories.Tattoo,   "tattoo-01",   "Художественная тату", "Авторский эскиз"),
            (WorkCategories.Pedicure, "pedicure-01", "Нюдовое покрытие", "Естественный оттенок"),
            (WorkCategories.Hair,     "hair-01",     "Локоны на длинные волосы", "Пляжная укладка"),
            (WorkCategories.Manicure, "manicure-02", "Дизайн с росписью", "Авторский дизайн"),
            (WorkCategories.Pedicure, "pedicure-02", "Уход за стопами", "Полная обработка"),
            (WorkCategories.Lashes,   "lashes-02",   "Наращивание 3D", "Выразительный взгляд"),
            (WorkCategories.Brows,    "brows-02",    "Ламинирование бровей", "Долговременная укладка"),
            (WorkCategories.Hair,     "hair-03",     "Вечерняя укладка", "Аккуратные пряди"),
            (WorkCategories.Hair,     "hair-04",     "Голливудские волны", "Мягкие локоны"),
            (WorkCategories.Hair,     "hair-05",     "Хвост и укладка", "Гладкий образ")
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Works (Title, Category, ImagePath, Description, MasterId, SortOrder, IsActive) " +
            "VALUES ($title, $cat, $img, $desc, NULL, $sort, 1)";
        var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
        var pCat = cmd.Parameters.Add("$cat", SqliteType.Text);
        var pImg = cmd.Parameters.Add("$img", SqliteType.Text);
        var pDesc = cmd.Parameters.Add("$desc", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sort", SqliteType.Integer);

        var order = 0;
        foreach (var w in works)
        {
            order++;
            pTitle.Value = w.Title;
            pCat.Value = w.Category;
            pImg.Value = $"/images/works/{w.Image}.jpg";
            pDesc.Value = w.Desc;
            pSort.Value = order;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Демонстрационные отзывы, которые кладутся в пустую базу, — чтобы блок «Отзывы»
    /// на главной не был пустым на первом запуске.
    /// </summary>
    /// <remarks>
    /// Список открыт наружу не для красоты: выдуманные отзывы — это недостоверная реклама
    /// (ч. 3 ст. 5 ФЗ «О рекламе»), поэтому админка обязана показать владельцу, что эти
    /// записи надо заменить настоящими или убрать, и именно по этому списку их узнаёт.
    /// </remarks>
    public static readonly (string Name, int Rating, string Text)[] DemoReviews =
    {
        ("Алина К.", 5, "Очень довольна работой мастера! Маникюр держится уже 3 недели, качество на высоте."),
        ("Мария С.", 5, "Уютная атмосфера, вежливый персонал и отличный результат. Обязательно приду ещё!"),
        ("Екатерина П.", 5, "Делала ресницы — просто влюбилась! Мастер очень внимательная, всё аккуратно и красиво."),
        ("Дарья Л.", 5, "Лучший салон в городе! Все мастера профессионалы, сервис на уровне.")
    };

    private static void SeedReviews(SqliteConnection connection)
    {
        if (!IsEmpty(connection, "Reviews")) return;

        var reviews = DemoReviews;

        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Reviews (ClientName, AvatarPath, Rating, Text, CreatedAt, SortOrder, IsActive) " +
            "VALUES ($name, $avatar, $rating, $text, $now, $sort, 1)";
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pAvatar = cmd.Parameters.Add("$avatar", SqliteType.Text);
        var pRating = cmd.Parameters.Add("$rating", SqliteType.Integer);
        var pText = cmd.Parameters.Add("$text", SqliteType.Text);
        var pNow = cmd.Parameters.Add("$now", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sort", SqliteType.Integer);

        var i = 0;
        foreach (var r in reviews)
        {
            i++;
            pName.Value = r.Name;
            pAvatar.Value = $"/images/reviews/r{i}.jpg";
            pRating.Value = r.Rating;
            pText.Value = r.Text;
            pNow.Value = DateTime.Now.AddDays(-i * 9).ToString("s");
            pSort.Value = i;
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedFaq(SqliteConnection connection)
    {
        if (!IsEmpty(connection, "FAQ")) return;

        var faq = new (string Q, string A)[]
        {
            ("Как отменить или перенести запись?",
                "Откройте ссылку из подтверждения записи — на странице управления можно перенести визит на другое время или отменить его. Также можно позвонить нам, и администратор всё поправит."),
            ("Сколько длится процедура?",
                "Зависит от услуги: маникюр — от 60 минут, педикюр — от 75, наращивание ресниц — около 2 часов, окрашивание волос — от 2,5 часов. Точное время указано в карточке услуги при записи."),
            ("Есть ли у вас подарочные сертификаты?",
                "Да, сертификаты доступны на любую сумму. Их можно оформить на ресепшене или по телефону — мы подготовим электронный вариант и передадим получателю."),
            ("Как подготовиться к маникюру / педикюру?",
                "Приходите без покрытия на ногтях, если планируете снятие, и предупредите мастера о повреждениях кожи. Удобная одежда и свободная обувь сделают визит комфортнее.")
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO FAQ (Question, Answer, SortOrder, IsActive) VALUES ($q, $a, $sort, 1)";
        var pQ = cmd.Parameters.Add("$q", SqliteType.Text);
        var pA = cmd.Parameters.Add("$a", SqliteType.Text);
        var pSort = cmd.Parameters.Add("$sort", SqliteType.Integer);

        var i = 0;
        foreach (var item in faq)
        {
            i++;
            pQ.Value = item.Q;
            pA.Value = item.A;
            pSort.Value = i;
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedSettings(SqliteConnection connection)
    {
        var defaults = new (string Key, string Value)[]
        {
            // Уведомления включены сразу. Раньше здесь стояло false, и на свежей базе
            // владелец не получал ничего: сайт молчал, пока галочку не найдут в настройках.
            // Выключить по-прежнему можно — галочка в /admin/settings на месте.
            ("Telegram.Enabled", "true"),
            ("Telegram.AdminChatId", ""),
            ("Telegram.BotUsername", ""),
            ("Telegram.AdminBotUsername", ""),
            ("Telegram.ReminderHoursBefore", "24"),
            ("Telegram.NotifyOnNewBooking", "true"),
            ("Clients.WinBackWeeks", "3"),
            ("Site.Phone", "+7 (999) 123-45-67"),
            ("Site.Address", "Москва, ул. Примерная, 12"),
            ("Site.Email", "hello@elora.ru"),
            // График должен совпадать с расписанием мастеров, которое кладёт SeedWorkingHours:
            // Пн–Сб 09:00–19:00, воскресенье — выходной. Раньше здесь стояло
            // «Ежедневно 09:00 – 21:00», и посетитель, придя в воскресенье к 21:00,
            // обнаружил бы закрытую дверь.
            ("Site.WorkHours", "Пн–Сб 09:00 – 19:00, Вс — выходной"),

            // Соцсети по умолчанию пустые. Раньше здесь стояли https://t.me, https://vk.com
            // и https://instagram.com, и подвал вёл на главные страницы чужих сервисов.
            // Ссылка на Instagram к тому же требует пометки о Meta (ч. 2 ст. 13.15 КоАП).
            ("Site.Telegram", ""),
            ("Site.Vk", ""),
            ("Site.Instagram", ""),

            // Реквизиты оператора персональных данных. Пусто — на странице политики
            // останется только название студии; владелец вписывает их в админке.
            ("Site.LegalName", ""),
            ("Site.LegalInn", "")
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Settings (Key, Value) VALUES ($k, $v)";
        var pK = cmd.Parameters.Add("$k", SqliteType.Text);
        var pV = cmd.Parameters.Add("$v", SqliteType.Text);
        foreach (var (key, value) in defaults)
        {
            pK.Value = key;
            pV.Value = value;
            cmd.ExecuteNonQuery();
        }

        ClearDemoSocialLinks(connection);
        EnableTelegramOnUntouchedDefault(connection);
    }

    /// <summary>
    /// Включает уведомления на базе, которую Telegram вообще не касался. Признак «не касался»
    /// строгий: галочка снята, Chat ID пуст и имена ботов не подтянуты. Раньше значение по
    /// умолчанию было <c>false</c>, и боевая база так и осталась с выключенными уведомлениями —
    /// сайт принимал записи и молчал. Если владелец что-то настроил, условие не сработает
    /// и ничего не переключится.
    /// </summary>
    private static void EnableTelegramOnUntouchedDefault(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Settings SET Value = 'true'
            WHERE Key = 'Telegram.Enabled' AND Value = 'false'
              AND (SELECT Value FROM Settings WHERE Key = 'Telegram.AdminChatId') = ''
              AND (SELECT Value FROM Settings WHERE Key = 'Telegram.BotUsername') = ''
              AND (SELECT Value FROM Settings WHERE Key = 'Telegram.AdminBotUsername') = '';
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Убирает демонстрационные ссылки на соцсети из уже созданных баз. INSERT OR IGNORE
    /// существующие строки не трогает, поэтому база боевого сайта продолжала отдавать
    /// в подвал https://instagram.com. Затираем только точное совпадение с демо-значением:
    /// если владелец вписал свою ссылку, она остаётся.
    /// </summary>
    private static void ClearDemoSocialLinks(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Settings SET Value = ''
            WHERE (Key = 'Site.Instagram' AND Value = 'https://instagram.com')
               OR (Key = 'Site.Vk'        AND Value = 'https://vk.com')
               OR (Key = 'Site.Telegram'  AND Value = 'https://t.me');
            """;
        cmd.ExecuteNonQuery();
    }
}
