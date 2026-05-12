using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class ReservationsModel(TravelCompanionDbContext dbContext) : PageModel
{
    public List<ReservationRow> Reservations { get; private set; } = [];
    public List<SelectListItem> TripOptions { get; private set; } = [];
    public List<SelectListItem> AccessLevelOptions { get; } = Enum.GetValues<ContentAccessLevel>()
        .Select(value => new SelectListItem(value.ToString(), value.ToString()))
        .ToList();

    [BindProperty]
    public ReservationInput Input { get; set; } = new();

    public async Task OnGetAsync(Guid? editId)
    {
        await LoadPageDataAsync();

        if (editId.HasValue)
        {
            var reservation = await dbContext.Reservations.FindAsync(editId.Value);
            if (reservation is not null)
            {
                Input = ReservationInput.FromEntity(reservation);
            }
        }
        else if (TripOptions.Count > 0)
        {
            Input.TripId = Guid.Parse(TripOptions[0].Value);
            Input.Date = DateOnly.FromDateTime(DateTime.Today);
            Input.StartsAt = new TimeOnly(9, 0);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        Reservation reservation;
        if (Input.Id.HasValue)
        {
            reservation = await dbContext.Reservations.FindAsync(Input.Id.Value)
                ?? throw new InvalidOperationException("Reservation not found.");
        }
        else
        {
            reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                TripId = Input.TripId,
                Title = string.Empty,
                LocationName = string.Empty,
                Address = string.Empty,
                ConfirmationCode = string.Empty,
                Notes = string.Empty
            };
            dbContext.Reservations.Add(reservation);
        }

        Input.ApplyTo(reservation);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var reservation = await dbContext.Reservations.FindAsync(id);
        if (reservation is not null)
        {
            dbContext.Reservations.Remove(reservation);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        TripOptions = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .OrderBy(trip => trip.StartsOn)
            .Select(trip => new SelectListItem(
                $"{trip.TravelerName} - {(trip.Destination != null ? trip.Destination.Name : "Unknown")}",
                trip.Id.ToString()))
            .ToListAsync();

        Reservations = await dbContext.Reservations
            .AsNoTracking()
            .Include(reservation => reservation.Trip)
            .ThenInclude(trip => trip!.Destination)
            .OrderBy(reservation => reservation.Date)
            .ThenBy(reservation => reservation.StartsAt)
            .Select(reservation => new ReservationRow(
                reservation.Id,
                reservation.Trip != null
                    ? $"{reservation.Trip.TravelerName} - {(reservation.Trip.Destination != null ? reservation.Trip.Destination.Name : "Unknown")}"
                    : "Unknown",
                reservation.Date,
                reservation.StartsAt,
                reservation.Title,
                reservation.LocationName,
                reservation.Address,
                reservation.AccessLevel,
                reservation.ConfirmationCode))
            .ToListAsync();
    }

    public sealed record ReservationRow(
        Guid Id,
        string TripName,
        DateOnly Date,
        TimeOnly StartsAt,
        string Title,
        string LocationName,
        string Address,
        ContentAccessLevel AccessLevel,
        string ConfirmationCode);

    public sealed class ReservationInput
    {
        public Guid? Id { get; set; }
        public Guid TripId { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly StartsAt { get; set; }
        public string Title { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string ConfirmationCode { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public ContentAccessLevel AccessLevel { get; set; } = ContentAccessLevel.Paid;

        public static ReservationInput FromEntity(Reservation reservation)
        {
            return new ReservationInput
            {
                Id = reservation.Id,
                TripId = reservation.TripId,
                Date = reservation.Date,
                StartsAt = reservation.StartsAt,
                Title = reservation.Title,
                LocationName = reservation.LocationName,
                Address = reservation.Address,
                ConfirmationCode = reservation.ConfirmationCode,
                Notes = reservation.Notes,
                AccessLevel = reservation.AccessLevel
            };
        }

        public void ApplyTo(Reservation reservation)
        {
            reservation.TripId = TripId;
            reservation.Date = Date;
            reservation.StartsAt = StartsAt;
            reservation.Title = Title.Trim();
            reservation.LocationName = LocationName.Trim();
            reservation.Address = Address.Trim();
            reservation.ConfirmationCode = ConfirmationCode.Trim();
            reservation.Notes = Notes.Trim();
            reservation.AccessLevel = AccessLevel;
        }
    }
}
