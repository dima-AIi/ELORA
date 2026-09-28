using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class BookingsModel : PageModel
{
    private readonly BookingRepository _bookings;
    private readonly MasterRepository _masters;
    private readonly BookingService _bookingService;

    public BookingsModel(BookingRepository bookings, MasterRepository masters, BookingService bookingService)
    {
        _bookings = bookings;
        _masters = masters;
        _bookingService = bookingService;
    }

    public List<Booking> Items { get; private set; } = new();
    public List<Master> Masters { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public int? MasterId { get; set; }

    public void OnGet()
    {
        Masters = _masters.GetAll(false);

        DateOnly? from = DateOnly.TryParse(From, out var f) ? f : null;
        DateOnly? to = DateOnly.TryParse(To, out var t) ? t : null;

        Items = _bookings.GetBookings(from, to, Status, MasterId);
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string status, CancellationToken cancellationToken)
    {
        // Через службу, а не напрямую в репозиторий: она же отправляет клиенту сообщение
        // о новом статусе. Прямая запись оставляла клиента в неведении.
        var error = await _bookingService.SetStatusAsync(id, status, cancellationToken);
        TempData[error is null ? "Flash" : "FlashError"] = error
            ?? $"Запись №{id}: {BookingStatuses.Title(status).ToLowerInvariant()}";

        return RedirectToPage(new { Status, From, To, MasterId });
    }

    /// <summary>
    /// Удаление записи насовсем. Разрешено только для закрытых: активную сначала отменяют,
    /// иначе одна кнопка в списке может унести живую запись клиента без следа.
    /// </summary>
    public IActionResult OnPostDelete(int id)
    {
        var booking = _bookings.GetById(id);
        if (booking is null)
        {
            TempData["FlashError"] = "Запись не найдена";
            return RedirectToPage(new { Status, From, To, MasterId });
        }

        if (booking.IsActiveStatus)
        {
            TempData["FlashError"] =
                $"Запись №{id} ещё активна. Сначала отмените её — удалять можно только закрытые записи.";
            return RedirectToPage(new { Status, From, To, MasterId });
        }

        var removed = _bookings.Delete(id);
        TempData[removed ? "Flash" : "FlashError"] = removed
            ? $"Запись №{id} удалена"
            : "Запись не найдена";

        return RedirectToPage(new { Status, From, To, MasterId });
    }
}
