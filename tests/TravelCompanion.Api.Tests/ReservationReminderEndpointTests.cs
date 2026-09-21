using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Controllers;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Tests;

public sealed class ReservationReminderEndpointTests
{
    [Fact]
    public async Task Requires_authentication()
    {
        await using var db = Database();
        var controller = Controller(db);
        Assert.IsType<UnauthorizedResult>((await controller.GetReminders("es", default)).Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_selected_owned_trip_exact_reservations_are_returned_and_deletion_removes_them(bool optInFlexible)
    {
        await using var db = Database();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "reminders@test.invalid", DisplayName = "Test" };
        db.AppUsers.Add(user);
        var trip = new Trip { Id = Guid.NewGuid(), AppUserId = user.Id, TravelerName = "Test", TimeZoneId = "Asia/Tokyo" };
        var another = new Trip { Id = Guid.NewGuid(), AppUserId = user.Id, TravelerName = "Other" };
        var foreign = new Trip { Id = Guid.NewGuid(), AppUserId = Guid.NewGuid(), TravelerName = "Foreign" };
        db.Trips.AddRange(trip, another, foreign);
        var reservation = Item(trip.Id);
        if (optInFlexible)
        {
            reservation.PlanningKind = ScheduleItemKind.ManualEvent;
            reservation.Flexibility = ItineraryFlexibility.Flexible;
            reservation.ReminderEnabled = true;
        }
        var optedOut = Item(trip.Id);
        optedOut.ReminderEnabled = false;
        db.Reservations.Add(optedOut);
        var flexible = Item(trip.Id); flexible.TimePrecision = ItineraryTimePrecision.PeriodOnly;
        db.Reservations.AddRange(reservation, flexible, Item(another.Id), Item(foreign.Id));
        await db.SaveChangesAsync();
        var sessions = new UserSessionService(db);
        var (_, token) = await sessions.CreateSessionAsync(user, tripId: trip.Id, accessMode: SessionAccessMode.FreeMapPreview);
        var controller = Controller(db, token);
        var reminders = Assert.IsAssignableFrom<IReadOnlyList<ReservationReminderDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetReminders("es", default)).Result).Value);
        Assert.Equal(2, reminders.Count);
        Assert.All(reminders, item => Assert.Equal(reservation.Id, item.ReservationId));
        db.Reservations.Remove(reservation);
        await db.SaveChangesAsync();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ReservationReminderDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetReminders("es", default)).Result).Value));
        trip.IsArchived = false;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var change = new TripDayPlan { Id = Guid.NewGuid(), TripId = trip.Id, Date = date.AddDays(1), City = "Kyoto" };
        db.TripDayPlans.AddRange(new TripDayPlan { Id = Guid.NewGuid(), TripId = trip.Id, Date = date, City = "Tokyo" }, change);
        await db.SaveChangesAsync();
        var withCityChange = Assert.IsAssignableFrom<IReadOnlyList<ReservationReminderDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetReminders("es", default)).Result).Value);
        Assert.Equal(change.Id, Assert.Single(withCityChange, item => item.ReservationId is null).TripDayId);
        trip.IsArchived = true;
        db.Reservations.Add(Item(trip.Id));
        await db.SaveChangesAsync();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<ReservationReminderDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetReminders("es", default)).Result).Value));
    }

    private static Reservation Item(Guid trip) => new()
    {
        Id = Guid.NewGuid(), TripId = trip, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), StartsAt = new(19, 0),
        Title = "Reservation", City = "Tokyo", LocationName = "Place", Address = "", ConfirmationCode = "", Notes = "",
        PlanningKind = ScheduleItemKind.ConfirmedReservation
    };
    private static TravelCompanionDbContext Database() => new(new DbContextOptionsBuilder<TravelCompanionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static NotificationsController Controller(TravelCompanionDbContext db, string? token = null)
    {
        var context = new DefaultHttpContext();
        if (token is not null) context.Request.Headers.Authorization = $"Bearer {token}";
        return new NotificationsController(db, new UserSessionService(db)) { ControllerContext = new() { HttpContext = context } };
    }
}
