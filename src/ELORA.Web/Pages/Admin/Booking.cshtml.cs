using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

/// <summary>
/// Карточка одной записи для администратора: кто записался, как с ним связаться и что
/// с записью можно сделать.
/// </summary>
/// <remarks>
/// Появилась после того, как владелец нажал в списке записей «Открыть» и попал на страницу
/// клиента: та говорит «Ваша запись» и предлагает клиентские кнопки. Администратору нужен
/// свой экран — с телефоном, ником в Telegram, заметкой о клиенте и кнопками управления.
/// Ссылка на клиентскую страницу осталась, но отдельной кнопкой и с честной подписью.
/// </remarks>
public class BookingCardModel : PageModel
{
    private readonly BookingRepository _bookings;
    private readonly ClientRepository _clients;
    private readonly BookingService _bookingService;

    public BookingCardModel(BookingRepository bookings, ClientRepository clients, BookingService bookingService)
    {
        _bookings = bookings;
        _clients = clients;
        _bookingService = bookingService;
    }

    public Booking Booking { get; private set; } = new();

    /// <summary>Клиент со сводкой: заметка администратора, число визитов, потраченная сумма.</summary>
    public ClientSummary? Client { get; private set; }

    /// <summary>Другие записи этого же клиента — без текущей.</summary>
    public List<Booking> OtherBookings { get; private set; } = new();

    public IActionResult OnGet(int id)
    {
        var booking = _bookings.GetById(id);
        if (booking is null) return NotFound();

        Booking = booking;
        Client = _clients.GetById(booking.ClientId);
        OtherBookings = _clients.GetBookings(booking.ClientId)
            .Where(item => item.Id != booking.Id)
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string status, CancellationToken cancellationToken)
    {
        // Через службу: она же отправляет клиенту сообщение о новом статусе.
        var error = await _bookingService.SetStatusAsync(id, status, cancellationToken);
        TempData[error is null ? "Flash" : "FlashError"] = error
            ?? $"Запись №{id}: {BookingStatuses.Title(status).ToLowerInvariant()}";

        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Удаление насовсем. То же правило, что в списке: активную запись сначала отменяют,
    /// иначе кнопка в карточке уносит живую запись клиента без следа.
    /// </summary>
    public IActionResult OnPostDelete(int id)
    {
        var booking = _bookings.GetById(id);
        if (booking is null)
        {
            TempData["FlashError"] = "Запись не найдена";
            return RedirectToPage("/Admin/Bookings");
        }

        if (booking.IsActiveStatus)
        {
            TempData["FlashError"] =
                $"Запись №{id} ещё активна. Сначала отмените её — удалять можно только закрытые записи.";
            return RedirectToPage(new { id });
        }

        var removed = _bookings.Delete(id);
        TempData[removed ? "Flash" : "FlashError"] = removed
            ? $"Запись №{id} удалена"
            : "Запись не найдена";

        return RedirectToPage("/Admin/Bookings");
    }
}
