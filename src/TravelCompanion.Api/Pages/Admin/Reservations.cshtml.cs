using System.ComponentModel.DataAnnotations;
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
    public List<TripRow> Trips { get; private set; } = [];
    public List<ReservationRow> Reservations { get; private set; } = [];
    public List<SelectListItem> TripOptions { get; private set; } = [];
    public List<SelectListItem> UserOptions { get; private set; } = [];
    public List<SelectListItem> DestinationOptions { get; private set; } = [];
    public List<SelectListItem> TypeOptions { get; } =
    [
        new("Evento", ReservationType.Event.ToString()),
        new("Vuelo", ReservationType.Flight.ToString()),
        new("Hospedaje", ReservationType.Lodging.ToString())
    ];
    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public ReservationInput Input { get; set; } = new();

    [BindProperty]
    public TripForm TripInput { get; set; } = new();

    public Guid? SelectedTripId { get; private set; }

    public async Task OnGetAsync(Guid? selectedTripId, Guid? editId, Guid? editTripId)
    {
        SelectedTripId = selectedTripId;
        await LoadPageDataAsync(selectedTripId);

        if (editTripId.HasValue)
        {
            var trip = await dbContext.Trips.FindAsync(editTripId.Value);
            if (trip is not null)
            {
                TripInput = TripForm.FromEntity(trip);
                SelectedTripId = trip.Id;
            }
        }
        else
        {
            SetDefaultTripFormValues();
        }

        if (editId.HasValue)
        {
            var reservation = await dbContext.Reservations.FindAsync(editId.Value);
            if (reservation is not null)
            {
                Input = ReservationInput.FromEntity(reservation);
                SelectedTripId = reservation.TripId;
            }
        }
        else
        {
            SetDefaultReservationFormValues();
        }
    }

    public async Task<IActionResult> OnPostSaveTripAsync()
    {
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.TripId)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Title)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.City)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.LocationName)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Address)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.ConfirmationCode)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Notes)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Airline)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.FlightNumber)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.OriginName)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.DestinationName)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.OriginAirport)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.DestinationAirport)}");

        if (TripInput.UserId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(TripInput)}.{nameof(TripInput.UserId)}", "Selecciona un usuario.");
        }

        if (TripInput.DestinationId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(TripInput)}.{nameof(TripInput.DestinationId)}", "Selecciona un destino.");
        }

        if (string.IsNullOrWhiteSpace(TripInput.TravelerName))
        {
            ModelState.AddModelError($"{nameof(TripInput)}.{nameof(TripInput.TravelerName)}", "El nombre del viajero es obligatorio.");
        }

        if (TripInput.EndsOn < TripInput.StartsOn)
        {
            ModelState.AddModelError($"{nameof(TripInput)}.{nameof(TripInput.EndsOn)}", "La fecha final no puede ser anterior al inicio.");
        }

        if (!ModelState.IsValid)
        {
            SelectedTripId = TripInput.Id;
            await LoadPageDataAsync(SelectedTripId);
            SetDefaultReservationFormValues();
            return Page();
        }

        Trip trip;
        if (TripInput.Id.HasValue)
        {
            trip = await dbContext.Trips.FindAsync(TripInput.Id.Value)
                ?? throw new InvalidOperationException("Trip not found.");
        }
        else
        {
            trip = new Trip
            {
                Id = Guid.NewGuid(),
                DestinationId = TripInput.DestinationId,
                TravelerName = string.Empty
            };
            dbContext.Trips.Add(trip);
        }

        TripInput.ApplyTo(trip);
        await dbContext.SaveChangesAsync();
        return RedirectToReservations(trip.Id);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        ModelState.Remove($"{nameof(TripInput)}.{nameof(TripInput.UserId)}");
        ModelState.Remove($"{nameof(TripInput)}.{nameof(TripInput.DestinationId)}");
        ModelState.Remove($"{nameof(TripInput)}.{nameof(TripInput.TravelerName)}");

        if (Input.TripId == Guid.Empty)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TripId)}", "Selecciona un viaje.");
        }

        if (string.IsNullOrWhiteSpace(Input.Title))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Title)}", "El titulo es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(Input.City))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.City)}", "La ciudad es obligatoria.");
        }

        if (Input.Type is not ReservationType.Flight && string.IsNullOrWhiteSpace(Input.LocationName))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.LocationName)}", "El lugar es obligatorio.");
        }

        if (Input.Type == ReservationType.Flight)
        {
            if (string.IsNullOrWhiteSpace(Input.FlightNumber))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.FlightNumber)}", "El numero de vuelo es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(Input.OriginName))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.OriginName)}", "El origen es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(Input.DestinationName))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.DestinationName)}", "El destino es obligatorio.");
            }
        }

        if (Input.Type == ReservationType.Lodging && Input.EndsOn is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.EndsOn)}", "La fecha de salida es obligatoria para hospedajes.");
        }

        if (!ModelState.IsValid)
        {
            SelectedTripId = Input.TripId;
            await LoadPageDataAsync(SelectedTripId);
            SetDefaultTripFormValues();
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
                Type = Input.Type,
                Title = string.Empty,
                City = string.Empty,
                LocationName = string.Empty,
                Address = string.Empty,
                ConfirmationCode = string.Empty,
                Notes = string.Empty
            };
            dbContext.Reservations.Add(reservation);
        }

        Input.ApplyTo(reservation);
        await dbContext.SaveChangesAsync();
        return RedirectToReservations(reservation.TripId);
    }

    public async Task<IActionResult> OnPostDeleteTripAsync(Guid id)
    {
        var trip = await dbContext.Trips
            .Include(existingTrip => existingTrip.Reservations)
            .FirstOrDefaultAsync(existingTrip => existingTrip.Id == id);

        if (trip is null)
        {
            return RedirectToPage();
        }

        if (trip.Reservations.Count > 0)
        {
            StatusMessage = "No se puede borrar un viaje con reservas. Borra sus reservas primero.";
            return RedirectToPage(new { selectedTripId = id });
        }

        dbContext.Trips.Remove(trip);
        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid? selectedTripId)
    {
        var reservation = await dbContext.Reservations.FindAsync(id);
        if (reservation is not null)
        {
            selectedTripId = reservation.TripId;
            dbContext.Reservations.Remove(reservation);
            await dbContext.SaveChangesAsync();
        }

        return selectedTripId.HasValue
            ? RedirectToReservations(selectedTripId.Value)
            : RedirectToPage();
    }

    private RedirectToPageResult RedirectToReservations(Guid selectedTripId)
    {
        return RedirectToPage(null, null, new { selectedTripId }, "reservations-list");
    }

    private async Task LoadPageDataAsync(Guid? selectedTripId)
    {
        UserOptions = await dbContext.AppUsers
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new SelectListItem($"{user.DisplayName} ({user.Email})", user.Id.ToString()))
            .ToListAsync();

        DestinationOptions = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Name)
            .Select(destination => new SelectListItem(destination.Name, destination.Id.ToString()))
            .ToListAsync();

        TripOptions = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.AppUser)
            .OrderByDescending(trip => trip.StartsOn)
            .Select(trip => new SelectListItem(
                $"{trip.TravelerName} - {(trip.Destination != null ? trip.Destination.Name : "Unknown")} ({trip.StartsOn:yyyy-MM-dd})",
                trip.Id.ToString()))
            .ToListAsync();

        Trips = await dbContext.Trips
            .AsNoTracking()
            .Include(trip => trip.Destination)
            .Include(trip => trip.AppUser)
            .Include(trip => trip.Reservations)
            .OrderByDescending(trip => trip.StartsOn)
            .Select(trip => new TripRow(
                trip.Id,
                trip.ExternalId,
                trip.AppUser != null ? trip.AppUser.Email : "-",
                trip.TravelerName,
                trip.Destination != null ? trip.Destination.Name : "Unknown",
                trip.StartsOn,
                trip.EndsOn,
                trip.Reservations.Count))
            .ToListAsync();

        var reservationsQuery = dbContext.Reservations
            .AsNoTracking()
            .Include(reservation => reservation.Trip)
                .ThenInclude(trip => trip!.Destination)
            .Include(reservation => reservation.Trip)
                .ThenInclude(trip => trip!.AppUser)
            .AsQueryable();

        if (selectedTripId.HasValue)
        {
            reservationsQuery = reservationsQuery.Where(reservation => reservation.TripId == selectedTripId.Value);
        }

        Reservations = await reservationsQuery
            .OrderBy(reservation => reservation.Date)
            .ThenBy(reservation => reservation.StartsAt)
            .Select(reservation => new ReservationRow(
                reservation.Id,
                reservation.ExternalId,
                reservation.Trip != null
                    ? $"{reservation.Trip.TravelerName} - {(reservation.Trip.Destination != null ? reservation.Trip.Destination.Name : "Unknown")}"
                    : "Unknown",
                reservation.Date,
                reservation.StartsAt,
                reservation.EndsOn,
                reservation.EndsAt,
                reservation.Type,
                reservation.Title,
                reservation.City,
                reservation.LocationName,
                reservation.Address,
                reservation.ConfirmationCode,
                reservation.Airline,
                reservation.FlightNumber,
                reservation.OriginName,
                reservation.DestinationName,
                reservation.OriginAirport,
                reservation.DestinationAirport,
                reservation.SourceName))
            .ToListAsync();
    }

    private void SetDefaultTripFormValues()
    {
        if (TripInput.UserId == Guid.Empty && UserOptions.Count > 0)
        {
            TripInput.UserId = Guid.Parse(UserOptions[0].Value);
        }

        if (TripInput.DestinationId == Guid.Empty && DestinationOptions.Count > 0)
        {
            TripInput.DestinationId = Guid.Parse(DestinationOptions[0].Value);
        }

        if (TripInput.StartsOn == default)
        {
            TripInput.StartsOn = DateOnly.FromDateTime(DateTime.Today);
        }

        if (TripInput.EndsOn == default)
        {
            TripInput.EndsOn = TripInput.StartsOn.AddDays(7);
        }
    }

    private void SetDefaultReservationFormValues()
    {
        if (Input.TripId == Guid.Empty)
        {
            if (SelectedTripId.HasValue)
            {
                Input.TripId = SelectedTripId.Value;
            }
            else if (TripOptions.Count > 0)
            {
                Input.TripId = Guid.Parse(TripOptions[0].Value);
            }
        }

        if (Input.Date == default)
        {
            Input.Date = DateOnly.FromDateTime(DateTime.Today);
        }

        if (Input.StartsAt == default)
        {
            Input.StartsAt = new TimeOnly(9, 0);
        }
    }

    public sealed record TripRow(
        Guid Id,
        string? ExternalId,
        string UserEmail,
        string TravelerName,
        string DestinationName,
        DateOnly StartsOn,
        DateOnly EndsOn,
        int ReservationCount);

    public sealed record ReservationRow(
        Guid Id,
        string? ExternalId,
        string TripName,
        DateOnly Date,
        TimeOnly StartsAt,
        DateOnly? EndsOn,
        TimeOnly? EndsAt,
        ReservationType Type,
        string Title,
        string City,
        string LocationName,
        string Address,
        string ConfirmationCode,
        string? Airline,
        string? FlightNumber,
        string? OriginName,
        string? DestinationName,
        string? OriginAirport,
        string? DestinationAirport,
        string? SourceName)
    {
        public string TypeLabel => Type switch
        {
            ReservationType.Flight => "Vuelo",
            ReservationType.Lodging => "Hospedaje",
            _ => "Evento"
        };

        public string PlaceLabel => Type == ReservationType.Flight
            ? $"{OriginName} -> {DestinationName}"
            : LocationName;
    }

    public sealed class ReservationInput
    {
        public Guid? Id { get; set; }

        [StringLength(160, ErrorMessage = "El external id no puede superar 160 caracteres.")]
        public string? ExternalId { get; set; }

        [Required(ErrorMessage = "Selecciona un viaje.")]
        public Guid TripId { get; set; }

        [Required(ErrorMessage = "Selecciona un tipo.")]
        public ReservationType Type { get; set; } = ReservationType.Event;

        [Required(ErrorMessage = "La fecha es obligatoria.")]
        public DateOnly Date { get; set; }

        [Required(ErrorMessage = "El horario de inicio es obligatorio.")]
        public TimeOnly StartsAt { get; set; }

        public DateOnly? EndsOn { get; set; }
        public TimeOnly? EndsAt { get; set; }

        [Required(ErrorMessage = "El titulo es obligatorio.")]
        [StringLength(160, ErrorMessage = "El titulo no puede superar 160 caracteres.")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "La ciudad es obligatoria.")]
        [StringLength(120, ErrorMessage = "La ciudad no puede superar 120 caracteres.")]
        public string City { get; set; } = string.Empty;

        [StringLength(180, ErrorMessage = "El lugar no puede superar 180 caracteres.")]
        public string? LocationName { get; set; }

        [StringLength(240, ErrorMessage = "La direccion no puede superar 240 caracteres.")]
        public string? Address { get; set; }

        [StringLength(80, ErrorMessage = "El codigo no puede superar 80 caracteres.")]
        public string? ConfirmationCode { get; set; }

        [StringLength(1000, ErrorMessage = "Las notas no pueden superar 1000 caracteres.")]
        public string? Notes { get; set; }

        [StringLength(120, ErrorMessage = "La aerolinea no puede superar 120 caracteres.")]
        public string? Airline { get; set; }

        [StringLength(40, ErrorMessage = "El numero de vuelo no puede superar 40 caracteres.")]
        public string? FlightNumber { get; set; }

        [StringLength(120, ErrorMessage = "El origen no puede superar 120 caracteres.")]
        public string? OriginName { get; set; }

        [StringLength(120, ErrorMessage = "El destino no puede superar 120 caracteres.")]
        public string? DestinationName { get; set; }

        [StringLength(80, ErrorMessage = "El aeropuerto de origen no puede superar 80 caracteres.")]
        public string? OriginAirport { get; set; }

        [StringLength(80, ErrorMessage = "El aeropuerto de destino no puede superar 80 caracteres.")]
        public string? DestinationAirport { get; set; }

        [StringLength(160, ErrorMessage = "La fuente no puede superar 160 caracteres.")]
        public string? SourceName { get; set; }

        [StringLength(512, ErrorMessage = "La URL fuente no puede superar 512 caracteres.")]
        public string? SourceUrl { get; set; }

        public static ReservationInput FromEntity(Reservation reservation)
        {
            return new ReservationInput
            {
                Id = reservation.Id,
                ExternalId = reservation.ExternalId,
                TripId = reservation.TripId,
                Type = reservation.Type,
                Date = reservation.Date,
                StartsAt = reservation.StartsAt,
                EndsOn = reservation.EndsOn,
                EndsAt = reservation.EndsAt,
                Title = reservation.Title,
                City = reservation.City,
                LocationName = reservation.LocationName,
                Address = reservation.Address,
                ConfirmationCode = reservation.ConfirmationCode,
                Notes = reservation.Notes,
                Airline = reservation.Airline,
                FlightNumber = reservation.FlightNumber,
                OriginName = reservation.OriginName,
                DestinationName = reservation.DestinationName,
                OriginAirport = reservation.OriginAirport,
                DestinationAirport = reservation.DestinationAirport,
                SourceName = reservation.SourceName,
                SourceUrl = reservation.SourceUrl
            };
        }

        public void ApplyTo(Reservation reservation)
        {
            reservation.ExternalId = NormalizeOptional(ExternalId);
            reservation.TripId = TripId;
            reservation.Type = Type;
            reservation.Date = Date;
            reservation.StartsAt = StartsAt;
            reservation.EndsOn = EndsOn;
            reservation.EndsAt = EndsAt;
            reservation.Title = Title.Trim();
            reservation.City = City.Trim();
            reservation.LocationName = NormalizeRequiredText(LocationName);
            reservation.Address = NormalizeRequiredText(Address);
            reservation.ConfirmationCode = NormalizeRequiredText(ConfirmationCode);
            reservation.Notes = NormalizeRequiredText(Notes);
            reservation.Airline = NormalizeOptional(Airline);
            reservation.FlightNumber = NormalizeOptional(FlightNumber);
            reservation.OriginName = NormalizeOptional(OriginName);
            reservation.DestinationName = NormalizeOptional(DestinationName);
            reservation.OriginAirport = NormalizeOptional(OriginAirport);
            reservation.DestinationAirport = NormalizeOptional(DestinationAirport);
            reservation.SourceName = NormalizeOptional(SourceName);
            reservation.SourceUrl = NormalizeOptional(SourceUrl);
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeRequiredText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class TripForm
    {
        public Guid? Id { get; set; }

        [StringLength(160, ErrorMessage = "El external id no puede superar 160 caracteres.")]
        public string? ExternalId { get; set; }

        [Required(ErrorMessage = "Selecciona un usuario.")]
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Selecciona un destino.")]
        public Guid DestinationId { get; set; }

        [Required(ErrorMessage = "El viajero es obligatorio.")]
        [StringLength(160, ErrorMessage = "El viajero no puede superar 160 caracteres.")]
        public string TravelerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha de inicio es obligatoria.")]
        public DateOnly StartsOn { get; set; }

        [Required(ErrorMessage = "La fecha final es obligatoria.")]
        public DateOnly EndsOn { get; set; }

        public static TripForm FromEntity(Trip trip)
        {
            return new TripForm
            {
                Id = trip.Id,
                ExternalId = trip.ExternalId,
                UserId = trip.AppUserId ?? Guid.Empty,
                DestinationId = trip.DestinationId,
                TravelerName = trip.TravelerName,
                StartsOn = trip.StartsOn,
                EndsOn = trip.EndsOn
            };
        }

        public void ApplyTo(Trip trip)
        {
            trip.ExternalId = string.IsNullOrWhiteSpace(ExternalId) ? null : ExternalId.Trim();
            trip.AppUserId = UserId;
            trip.DestinationId = DestinationId;
            trip.TravelerName = (TravelerName ?? string.Empty).Trim();
            trip.StartsOn = StartsOn;
            trip.EndsOn = EndsOn;
        }
    }
}
