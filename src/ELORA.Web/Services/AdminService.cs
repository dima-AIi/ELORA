using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Services;

/// <summary>Операции админ-панели, не относящиеся к записям напрямую.</summary>
public sealed class AdminService
{
    private readonly BookingRepository _bookings;
    private readonly CatalogRepository _catalog;
    private readonly MasterRepository _masters;
    private readonly ContentRepository _content;
    private readonly SettingsRepository _settings;
    private readonly AdminUserRepository _users;

    public AdminService(
        BookingRepository bookings,
        CatalogRepository catalog,
        MasterRepository masters,
        ContentRepository content,
        SettingsRepository settings,
        AdminUserRepository users)
    {
        _bookings = bookings;
        _catalog = catalog;
        _masters = masters;
        _content = content;
        _settings = settings;
        _users = users;
    }

    public DashboardStats GetDashboard()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var todayBookings = _bookings.GetBookings(today, today);

        return new DashboardStats
        {
            TodayCount = todayBookings.Count,
            TodayActiveCount = todayBookings.Count(b => b.IsActiveStatus),
            PendingCount = _bookings.CountByStatus(BookingStatuses.Pending),
            ConfirmedCount = _bookings.CountByStatus(BookingStatuses.Confirmed),
            CancelledCount = _bookings.CountByStatus(BookingStatuses.Cancelled),
            TotalCount = _bookings.CountAll(),
            MonthRevenue = _bookings.RevenueForPeriod(monthStart, today),
            ServicesCount = _catalog.GetServices().Count,
            MastersCount = _masters.GetAll().Count,
            WorksCount = _content.GetWorks(null, true).Count,
            TodayBookings = todayBookings.OrderBy(b => b.StartAt).ToList(),
            Upcoming = _bookings.GetBookings(today, today.AddDays(7))
                .Where(b => b.IsActiveStatus)
                .OrderBy(b => b.StartAt)
                .Take(8)
                .ToList()
        };
    }

    public bool VerifyLogin(string login, string password, out int userId)
    {
        userId = 0;
        var user = _users.FindByLogin(login);
        if (user is null) return false;
        if (!PasswordHasher.Verify(password, user.Value.PasswordHash)) return false;
        userId = user.Value.Id;
        return true;
    }

    public void ChangePassword(int userId, string newPassword) =>
        _users.UpdatePassword(userId, PasswordHasher.Hash(newPassword));

    public Dictionary<string, string> GetSettings() => _settings.GetAll();

    public void SaveSettings(IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var pair in values) _settings.Set(pair.Key, pair.Value);
    }

    public void Set(string key, string value) => _settings.Set(key, value);

    public string Get(string key, string fallback = "") => _settings.Get(key, fallback);
}

public sealed class DashboardStats
{
    public int TodayCount { get; set; }
    public int TodayActiveCount { get; set; }
    public int PendingCount { get; set; }
    public int ConfirmedCount { get; set; }
    public int CancelledCount { get; set; }
    public int TotalCount { get; set; }
    public decimal MonthRevenue { get; set; }
    public int ServicesCount { get; set; }
    public int MastersCount { get; set; }
    public int WorksCount { get; set; }
    public List<Booking> TodayBookings { get; set; } = new();
    public List<Booking> Upcoming { get; set; } = new();
}
