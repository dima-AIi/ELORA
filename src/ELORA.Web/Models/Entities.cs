namespace ELORA.Web.Models;

/// <summary>Категория услуг (Маникюр, Педикюр, ...). Используется для карточек на главной и фильтров.</summary>
public class ServiceCategory
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public decimal PriceFrom { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Конкретная услуга, на которую можно записаться.</summary>
public class Service
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategorySlug { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Master
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Phone { get; set; }
    public string? TelegramChatId { get; set; }
    public string? PhotoPath { get; set; }
    public string? Specialization { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public List<int> ServiceIds { get; set; } = new();
}

public class WorkingHours
{
    public int Id { get; set; }
    public int MasterId { get; set; }
    public int DayOfWeek { get; set; } // 0 = воскресенье (как DayOfWeek в .NET)
    public string StartTime { get; set; } = "09:00";
    public string EndTime { get; set; } = "18:00";
    public bool IsActive { get; set; } = true;
}

public class BlockedDate
{
    public int Id { get; set; }
    public int? MasterId { get; set; }
    public string Date { get; set; } = ""; // yyyy-MM-dd
    public string? Reason { get; set; }
}

public class Work
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string? Description { get; set; }
    public int? MasterId { get; set; }
    public string? MasterName { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Review
{
    public int Id { get; set; }
    public string ClientName { get; set; } = "";
    public string? AvatarPath { get; set; }
    public int Rating { get; set; } = 5;
    public string Text { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FaqItem
{
    public int Id { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? TelegramChatId { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class Booking
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public int MasterId { get; set; }
    public int ServiceId { get; set; }
    public string StartAt { get; set; } = "";
    public string EndAt { get; set; } = "";
    public string Status { get; set; } = BookingStatuses.Pending;
    public string? Comment { get; set; }
    public string ManageToken { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    // Развёрнутые данные для отображения
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public string? ClientTelegramChatId { get; set; }

    /// <summary>Ник клиента в Telegram в виде <c>@nick</c>, если он его указывал.</summary>
    public string? ClientTelegramNick { get; set; }
    public string MasterName { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string ServiceCategory { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }

    public DateTime StartLocal => DateTime.TryParse(StartAt, out var d) ? d : DateTime.MinValue;
    public DateTime EndLocal => DateTime.TryParse(EndAt, out var d) ? d : DateTime.MinValue;
    public bool IsActiveStatus => Status is BookingStatuses.Pending or BookingStatuses.Confirmed;
}

public class ReminderLog
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public string ReminderType { get; set; } = "";
    public string SentAt { get; set; } = "";
}
