using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed partial class TripWorkbookImportService(
    TravelCompanionDbContext dbContext,
    IPasswordHasher<Trip> tripPinHasher,
    ILogger<TripWorkbookImportService> logger)
{
    public const string SourceName = "Trip Excel Import";

    private const string CreateTripSheetName = "Crear viaje";
    private const string CatalogSheetName = "CatalogoRecommendations";
    private const string DropdownSheetName = "ListasDropdown";
    private const string ValidationsSheetName = "Validaciones";
    private const int MetadataValueColumn = 2;
    private const int HeaderRowNumber = 9;
    private const int FirstDataRowNumber = HeaderRowNumber + 1;
    private const int MaxTemplateRows = 120;
    private const int HelperListColumn = 13;
    private const string DefaultDestinationSlug = "japon";
    private const string DefaultTimezone = "Asia/Tokyo";

    private static readonly string[] Headers =
    [
        "Dia",
        "Fecha",
        "Ciudad",
        "Hotel/Base",
        "Momento",
        "Descripcion curada",
        "Location 1",
        "Location 2",
        "Location 3",
        "Reserva",
        "Hora reserva",
        "Notas"
    ];

    public async Task<byte[]> CreateTemplateAsync(CancellationToken cancellationToken = default)
    {
        var destinations = await dbContext.Destinations
            .AsNoTracking()
            .OrderBy(destination => destination.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var recommendations = await LoadRecommendationCatalogAsync(cancellationToken)
            .ConfigureAwait(false);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(CreateTripSheetName);
        var catalogSheet = workbook.AddWorksheet(CatalogSheetName);
        var dropdownSheet = workbook.AddWorksheet(DropdownSheetName);
        var validationsSheet = workbook.AddWorksheet(ValidationsSheetName);

        BuildMainSheet(sheet);
        BuildCatalogSheet(catalogSheet, recommendations);
        BuildValidationsSheet(validationsSheet, destinations, recommendations);
        BuildDropdownSheet(workbook, dropdownSheet, recommendations);
        ApplyMainSheetValidations(workbook, sheet, validationsSheet, destinations);

        catalogSheet.Visibility = XLWorksheetVisibility.Hidden;
        dropdownSheet.Visibility = XLWorksheetVisibility.Hidden;
        validationsSheet.Visibility = XLWorksheetVisibility.Hidden;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> CreateExampleWorkbookAsync(CancellationToken cancellationToken = default)
    {
        var recommendations = await LoadRecommendationCatalogAsync(cancellationToken)
            .ConfigureAwait(false);
        var bytes = await CreateTemplateAsync(cancellationToken).ConfigureAwait(false);

        using var input = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheet(CreateTripSheetName);
        var startsOn = new DateOnly(2026, 10, 1);
        var endsOn = startsOn.AddDays(17);

        sheet.Cell(1, MetadataValueColumn).Value = "1908";
        sheet.Cell(2, MetadataValueColumn).Value = "Cliente ejemplo Japon 18 dias";
        sheet.Cell(3, MetadataValueColumn).Value = DefaultDestinationSlug;
        sheet.Cell(4, MetadataValueColumn).Value = startsOn.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(5, MetadataValueColumn).Value = endsOn.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(6, MetadataValueColumn).Value = DefaultTimezone;
        sheet.Range(4, MetadataValueColumn, 5, MetadataValueColumn).Style.DateFormat.Format = "yyyy-mm-dd";

        var rows = CreateExampleRows(recommendations);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = FirstDataRowNumber + index;
            sheet.Cell(excelRow, 1).Value = row.Day;
            sheet.Cell(excelRow, 2).Value = startsOn.AddDays(row.Day - 1).ToDateTime(TimeOnly.MinValue);
            sheet.Cell(excelRow, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(excelRow, 3).Value = row.City;
            sheet.Cell(excelRow, 4).Value = row.HotelBase;
            sheet.Cell(excelRow, 5).Value = row.Moment;
            sheet.Cell(excelRow, 6).Value = row.CuratedDescription;
            sheet.Cell(excelRow, 7).Value = OptionalExampleText(row.Location1);
            sheet.Cell(excelRow, 8).Value = OptionalExampleText(row.Location2);
            sheet.Cell(excelRow, 9).Value = OptionalExampleText(row.Location3);
            sheet.Cell(excelRow, 10).Value = row.IsReservation ? "Si" : "No";
            sheet.Cell(excelRow, 11).Value = row.Time ?? string.Empty;
            sheet.Cell(excelRow, 12).Value = OptionalExampleText(row.Notes);
        }

        sheet.Columns(1, 12).AdjustToContents();
        sheet.Column(6).Width = 46;
        sheet.Column(7).Width = 38;
        sheet.Column(8).Width = 38;
        sheet.Column(9).Width = 38;
        sheet.Column(12).Width = 34;
        var firstRow = FirstDataRowNumber;
        var lastRow = FirstDataRowNumber + rows.Count - 1;
        sheet.Range(firstRow, 6, lastRow, 6).Style.Alignment.WrapText = true;
        sheet.Range(firstRow, 12, lastRow, 12).Style.Alignment.WrapText = true;
        sheet.Rows(firstRow, lastRow).Height = 36;
        sheet.Range(firstRow, 1, lastRow, 12).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static string OptionalExampleText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    public async Task<TripWorkbookImportResult> PreviewAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        var parseResult = await ParseAsync(workbookStream, cancellationToken).ConfigureAwait(false);
        return parseResult.Result;
    }

    public async Task<TripWorkbookImportResult> ImportAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        var parseResult = await ParseAsync(workbookStream, cancellationToken).ConfigureAwait(false);
        var result = parseResult.Result;
        if (result.HasErrors || parseResult.Metadata is null)
        {
            return result with
            {
                StatusMessage = "No se importo nada porque el archivo tiene errores."
            };
        }

        var metadata = parseResult.Metadata;
        var destination = await dbContext.Destinations
            .FirstOrDefaultAsync(existingDestination => existingDestination.Id == metadata.DestinationId, cancellationToken)
            .ConfigureAwait(false);
        if (destination is null)
        {
            return result with
            {
                Errors = [.. result.Errors, "El destino ya no existe."],
                StatusMessage = "No se importo nada porque falta el destino."
            };
        }

        var userEmail = CreateTripUserEmail(metadata);
        var trip = await dbContext.Trips
            .Include(existingTrip => existingTrip.Reservations)
            .FirstOrDefaultAsync(existingTrip =>
                existingTrip.DestinationId == metadata.DestinationId
                && existingTrip.ExternalId == metadata.TripExternalId,
                cancellationToken)
            .ConfigureAwait(false);

        var createdTrip = trip is null;
        var user = trip?.AppUserId is not null
            ? await dbContext.AppUsers.FirstOrDefaultAsync(existingUser => existingUser.Id == trip.AppUserId.Value, cancellationToken)
                .ConfigureAwait(false)
            : null;
        user ??= await dbContext.AppUsers
            .FirstOrDefaultAsync(existingUser => existingUser.Email == userEmail, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = userEmail,
                DisplayName = metadata.TravelerName,
                PasswordHash = string.Empty,
                MustChangePassword = false
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.DisplayName = metadata.TravelerName;
            user.MustChangePassword = false;
        }

        if (trip is null)
        {
            trip = new Trip
            {
                Id = Guid.NewGuid(),
                ExternalId = metadata.TripExternalId,
                AppUserId = user.Id,
                DestinationId = metadata.DestinationId,
                TravelerName = metadata.TravelerName,
                StartsOn = metadata.StartsOn,
                EndsOn = metadata.EndsOn,
                TimeZoneId = metadata.TimeZoneId
            };
            dbContext.Trips.Add(trip);
        }

        trip.AppUserId = user.Id;
        trip.DestinationId = metadata.DestinationId;
        trip.TravelerName = metadata.TravelerName;
        trip.StartsOn = metadata.StartsOn;
        trip.EndsOn = metadata.EndsOn;
        trip.TimeZoneId = metadata.TimeZoneId;
        trip.AccessPinHash = tripPinHasher.HashPassword(trip, metadata.Pin);
        trip.AccessPinUpdatedAt = DateTimeOffset.UtcNow;

        if (!createdTrip)
        {
            var importedReservations = trip.Reservations
                .Where(reservation => string.Equals(reservation.SourceName, SourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            dbContext.Reservations.RemoveRange(importedReservations);
        }

        var reservations = CreateReservations(trip, destination, metadata, parseResult.Rows);
        foreach (var reservation in reservations)
        {
            dbContext.Reservations.Add(reservation);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var lodgingCount = reservations.Count(reservation => reservation.Type == ReservationType.Lodging);
        logger.LogInformation(
            "Trip workbook import complete. TripId={TripId}; ExternalId={ExternalId}; CreatedTrip={CreatedTrip}; ReservationsCreated={ReservationCount}; LodgingCreated={LodgingCount}; AutofillRows={AutofillRows}.",
            trip.Id,
            metadata.TripExternalId,
            createdTrip,
            reservations.Count,
            lodgingCount,
            result.AutofillRows);

        return result with
        {
            Imported = true,
            CreatedTrip = createdTrip,
            UpdatedTrip = !createdTrip,
            CreatedReservations = reservations.Count,
            CreatedLodgingReservations = lodgingCount,
            StatusMessage = $"Import completo. Viaje: {(createdTrip ? "creado" : "actualizado")}; reservas creadas: {reservations.Count}; bloques autofill: {result.AutofillRows}; warnings: {result.WarningCount}."
        };
    }

    private async Task<ParsedTripWorkbook> ParseAsync(
        Stream workbookStream,
        CancellationToken cancellationToken)
    {
        var recommendations = await LoadRecommendationCatalogAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<string>();
        var rows = new List<TripWorkbookImportRow>();
        var drafts = new List<TripWorkbookRowDraft>();

        using var workbook = new XLWorkbook(workbookStream);
        var sheet = workbook.Worksheets.FirstOrDefault(worksheet =>
            string.Equals(worksheet.Name, CreateTripSheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            return new ParsedTripWorkbook(null, [], CreateResult(null, [], [$"El archivo debe tener una hoja visible llamada '{CreateTripSheetName}'."], imported: false));
        }

        var metadata = await ReadMetadataAsync(sheet, errors, cancellationToken).ConfigureAwait(false);
        var headerMap = CreateHeaderMap(sheet.Row(HeaderRowNumber));
        AddLegacyHeaderAlias(headerMap, "Hora reserva", "Hora");
        foreach (var header in Headers)
        {
            if (!headerMap.ContainsKey(NormalizeHeader(header)))
            {
                errors.Add($"Falta la columna obligatoria '{header}' en la fila {HeaderRowNumber}.");
            }
        }

        if (errors.Count > 0 || metadata is null)
        {
            return new ParsedTripWorkbook(metadata, [], CreateResult(metadata, rows, errors, imported: false));
        }

        var seenRowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recommendationCatalog = recommendations
            .GroupBy(item => NormalizeSearchText(item.DisplayName))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var recommendationsByCityAndTitle = recommendations
            .GroupBy(item => $"{NormalizeSearchText(item.City)}|{NormalizeSearchText(item.Title)}")
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var recommendationsByTitle = recommendations
            .GroupBy(item => NormalizeSearchText(item.Title))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var recommendationsByExternalId = recommendations
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId))
            .GroupBy(item => NormalizeSearchText(item.ExternalId!))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var lastRow = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? FirstDataRowNumber - 1, FirstDataRowNumber - 1);
        for (var rowNumber = FirstDataRowNumber; rowNumber <= lastRow; rowNumber++)
        {
            var excelRow = sheet.Row(rowNumber);
            if (IsBlankRow(excelRow, headerMap))
            {
                continue;
            }

            var rowErrors = new List<string>();
            var rowWarnings = new List<string>();
            var dayText = ReadCell(excelRow, headerMap, "Dia");
            var city = ReadCell(excelRow, headerMap, "Ciudad");
            var hotelBase = NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Hotel/Base"));
            var moment = ReadCell(excelRow, headerMap, "Momento");
            var curatedDescription = NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Descripcion curada"));
            var reservationText = NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Reserva"));
            var notes = NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Notas"));
            var locationInputs = new[]
                {
                    NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Location 1")),
                    NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Location 2")),
                    NormalizeOptionalValue(ReadCell(excelRow, headerMap, "Location 3"))
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToList();

            if (!int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) || day <= 0)
            {
                rowErrors.Add($"Fila {rowNumber}: Dia debe ser un numero mayor a cero.");
                day = 0;
            }

            var date = day > 0 ? metadata.StartsOn.AddDays(day - 1) : metadata.StartsOn;
            if (day > 0 && (date < metadata.StartsOn || date > metadata.EndsOn))
            {
                rowErrors.Add($"Fila {rowNumber}: Dia {day} queda fuera del rango del viaje.");
            }

            Require(rowErrors, rowNumber, "Ciudad", city);
            var period = ResolvePeriod(moment);
            if (period is null)
            {
                rowErrors.Add($"Fila {rowNumber}: Momento debe ser Manana, Medio dia, Tarde o Noche.");
            }

            var isReservation = ParseReservationFlag(reservationText, rowWarnings);
            var parsedStartTime = ParseTime(excelRow, headerMap, "Hora reserva", rowWarnings);
            if (isReservation && !parsedStartTime.HasValue)
            {
                rowErrors.Add($"Fila {rowNumber}: Hora reserva es obligatoria cuando Reserva es Si.");
            }

            if (!isReservation && parsedStartTime.HasValue)
            {
                rowWarnings.Add("La hora se ignoro porque Reserva es No; Momento define la franja de la recomendacion.");
            }

            var startTime = isReservation ? parsedStartTime : null;
            var locationMatches = locationInputs
                .Select(input => MatchLocation(
                    input,
                    city,
                    recommendationCatalog,
                    recommendationsByCityAndTitle,
                    recommendationsByTitle,
                    recommendationsByExternalId,
                    rowWarnings))
                .ToList();

            var hasScheduledContent = !string.IsNullOrWhiteSpace(curatedDescription)
                || locationInputs.Count > 0
                || isReservation
                || startTime.HasValue;
            var isAutofill = !hasScheduledContent || IsExplicitFreeBlock(curatedDescription);
            if (isAutofill && (locationInputs.Count > 0 || isReservation || startTime.HasValue))
            {
                rowWarnings.Add("El bloque esta marcado como autofill pero incluye locations, reserva u horario; esos datos no se importaran.");
            }

            if (!isAutofill && locationInputs.Count == 0 && string.IsNullOrWhiteSpace(curatedDescription))
            {
                rowWarnings.Add("La fila no tiene locations ni descripcion; se importara como bloque generico.");
            }

            if (period is not null && day > 0)
            {
                var rowKey = $"{day}|{period.Key}|{string.Join('|', locationInputs.Select(NormalizeSearchText))}|{NormalizeSearchText(curatedDescription)}";
                if (!seenRowKeys.Add(rowKey))
                {
                    rowWarnings.Add("Bloque duplicado dentro del Excel.");
                }
            }

            var previewRow = new TripWorkbookImportRow(
                rowNumber,
                day,
                date,
                city.Trim(),
                hotelBase.Trim(),
                period?.Label ?? moment,
                curatedDescription.Trim(),
                locationInputs,
                locationMatches.Select(match => match.ToPreview()).ToList(),
                isReservation,
                startTime?.ToString("HH\\:mm", CultureInfo.InvariantCulture),
                notes.Trim(),
                isAutofill,
                rowErrors,
                rowWarnings);
            rows.Add(previewRow);
            errors.AddRange(rowErrors);

            if (rowErrors.Count == 0 && period is not null)
            {
                drafts.Add(new TripWorkbookRowDraft(
                    rowNumber,
                    day,
                    date,
                    city.Trim(),
                    hotelBase.Trim(),
                    period,
                    curatedDescription.Trim(),
                    locationMatches,
                    isReservation,
                    startTime,
                    notes.Trim(),
                    isAutofill));
            }
        }

        return new ParsedTripWorkbook(
            metadata,
            drafts,
            CreateResult(metadata, rows, errors, imported: false));
    }

    private async Task<TripWorkbookMetadata?> ReadMetadataAsync(
        IXLWorksheet sheet,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var pin = sheet.Cell(1, MetadataValueColumn).GetFormattedString().Trim();
        var travelerName = sheet.Cell(2, MetadataValueColumn).GetFormattedString().Trim();
        var destinationValue = sheet.Cell(3, MetadataValueColumn).GetFormattedString().Trim();
        var timezone = sheet.Cell(6, MetadataValueColumn).GetFormattedString().Trim();

        if (pin.Length != 4 || pin.Any(character => !char.IsDigit(character)))
        {
            errors.Add("PIN debe tener exactamente 4 numeros.");
        }

        if (string.IsNullOrWhiteSpace(travelerName))
        {
            errors.Add("Nombre cliente es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(destinationValue))
        {
            errors.Add("Destino es obligatorio.");
        }

        if (!TryReadDate(sheet.Cell(4, MetadataValueColumn), out var startsOn))
        {
            errors.Add("Fecha inicio es obligatoria y debe ser una fecha valida.");
        }

        if (!TryReadDate(sheet.Cell(5, MetadataValueColumn), out var endsOn))
        {
            errors.Add("Fecha fin es obligatoria y debe ser una fecha valida.");
        }

        if (startsOn != default && endsOn != default && endsOn < startsOn)
        {
            errors.Add("Fecha fin no puede ser anterior a Fecha inicio.");
        }

        if (string.IsNullOrWhiteSpace(timezone))
        {
            timezone = DefaultTimezone;
        }

        var normalizedDestination = NormalizeSearchText(destinationValue);
        var destinations = await dbContext.Destinations
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var destination = destinations.FirstOrDefault(existingDestination =>
            NormalizeSearchText(existingDestination.Slug) == normalizedDestination
            || NormalizeSearchText(existingDestination.Name) == normalizedDestination);

        if (destination is null && !string.IsNullOrWhiteSpace(destinationValue))
        {
            errors.Add($"No existe el destino '{destinationValue}'. Usa el slug o nombre configurado en Admin.");
        }

        if (errors.Count > 0 || destination is null)
        {
            return null;
        }

        return new TripWorkbookMetadata(
            pin,
            travelerName.Trim(),
            destination.Id,
            destination.Slug,
            startsOn,
            endsOn,
            timezone.Trim(),
            CreateTripExternalId(destination.Slug, pin, startsOn, endsOn));
    }

    private IReadOnlyList<Reservation> CreateReservations(
        Trip trip,
        Destination destination,
        TripWorkbookMetadata metadata,
        IReadOnlyList<TripWorkbookRowDraft> rows)
    {
        var reservations = new List<Reservation>();
        reservations.AddRange(CreateLodgingReservations(trip.Id, destination, metadata, rows));

        foreach (var row in rows.Where(row => !row.IsAutofill))
        {
            var startsAt = row.StartTime ?? row.Period.DefaultStart;
            var locationMatches = row.LocationMatches.Count > 0
                ? row.LocationMatches
                : [LocationMatch.Unmatched(row.CuratedDescription, "Bloque generico sin location")];

            for (var index = 0; index < locationMatches.Count; index++)
            {
                var match = locationMatches[index];
                var recommendation = match.Recommendation;
                var title = CreateReservationTitle(row, match);
                var durationMinutes = recommendation?.SuggestedDurationMinutes > 0
                    ? recommendation.SuggestedDurationMinutes
                    : row.Period.DefaultDurationMinutes;
                var endsAt = startsAt.AddMinutes(durationMinutes);
                reservations.Add(new Reservation
                {
                    Id = Guid.NewGuid(),
                    TripId = trip.Id,
                    ExternalId = TrimToMax(
                        $"{SourceSlug(metadata)}-d{row.Day:D2}-{row.Period.Key}-{index + 1:D2}-{Slugify(title)}",
                        160),
                    RecommendationId = recommendation?.Id,
                    Type = ReservationType.Event,
                    PlanningKind = row.IsReservation
                        ? ScheduleItemKind.ConfirmedReservation
                        : ScheduleItemKind.Recommendation,
                    Date = row.Date,
                    StartsAt = startsAt,
                    EndsAt = endsAt,
                    TimeZoneId = metadata.TimeZoneId,
                    Title = title,
                    City = row.City,
                    LocationName = recommendation?.Title ?? match.Input,
                    Address = recommendation?.Neighborhood ?? row.City,
                    ConfirmationCode = string.Empty,
                    Notes = CreateReservationNotes(row.CuratedDescription, row.Notes),
                    SourceName = SourceName,
                    SourceUrl = recommendation?.SourceUrl
                });
                startsAt = endsAt.AddMinutes(15);
            }
        }

        return reservations;
    }

    private static IReadOnlyList<Reservation> CreateLodgingReservations(
        Guid tripId,
        Destination destination,
        TripWorkbookMetadata metadata,
        IReadOnlyList<TripWorkbookRowDraft> rows)
    {
        var days = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.HotelBase))
            .GroupBy(row => row.Day)
            .Select(group => group.First())
            .OrderBy(row => row.Date)
            .ToList();
        var reservations = new List<Reservation>();
        if (days.Count == 0)
        {
            return reservations;
        }

        var current = days[0];
        var segmentStart = current.Date;
        var segmentEnd = current.Date;
        var hotel = current.HotelBase;
        var city = current.City;

        foreach (var day in days.Skip(1))
        {
            if (day.Date == segmentEnd.AddDays(1)
                && string.Equals(day.HotelBase, hotel, StringComparison.OrdinalIgnoreCase)
                && string.Equals(day.City, city, StringComparison.OrdinalIgnoreCase))
            {
                segmentEnd = day.Date;
                continue;
            }

            reservations.Add(CreateLodgingReservation(tripId, destination, metadata, segmentStart, segmentEnd, hotel, city));
            segmentStart = day.Date;
            segmentEnd = day.Date;
            hotel = day.HotelBase;
            city = day.City;
        }

        reservations.Add(CreateLodgingReservation(tripId, destination, metadata, segmentStart, segmentEnd, hotel, city));
        return reservations;
    }

    private static Reservation CreateLodgingReservation(
        Guid tripId,
        Destination destination,
        TripWorkbookMetadata metadata,
        DateOnly startsOn,
        DateOnly lastNight,
        string hotel,
        string city)
    {
        var endsOn = lastNight.AddDays(1);
        if (endsOn > metadata.EndsOn)
        {
            endsOn = metadata.EndsOn;
        }

        return new Reservation
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            ExternalId = TrimToMax($"{SourceSlug(metadata)}-lodging-{startsOn:yyyyMMdd}-{Slugify(hotel)}", 160),
            Type = ReservationType.Lodging,
            PlanningKind = ScheduleItemKind.ConfirmedReservation,
            Date = startsOn,
            StartsAt = new TimeOnly(15, 0),
            EndsOn = endsOn,
            EndsAt = new TimeOnly(11, 0),
            TimeZoneId = metadata.TimeZoneId,
            Title = hotel,
            City = city,
            LocationName = hotel,
            Address = string.IsNullOrWhiteSpace(city) ? destination.Name : city,
            ConfirmationCode = string.Empty,
            Notes = "Base cargada desde Excel.",
            SourceName = SourceName
        };
    }

    private async Task<IReadOnlyList<RecommendationCatalogItem>> LoadRecommendationCatalogAsync(
        CancellationToken cancellationToken)
    {
        var recommendations = await dbContext.Recommendations
            .AsNoTracking()
            .OrderBy(recommendation => recommendation.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var displayCounts = recommendations
            .Select(recommendation => CreateDisplayName(recommendation, includeExternalId: false))
            .GroupBy(displayName => displayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return recommendations
            .Select(recommendation =>
            {
                var simpleDisplayName = CreateDisplayName(recommendation, includeExternalId: false);
                var displayName = displayCounts[simpleDisplayName] > 1
                    ? CreateDisplayName(recommendation, includeExternalId: true)
                    : simpleDisplayName;
                var city = ResolveRecommendationCity(recommendation);
                var periods = ResolvePeriodKeys(recommendation);
                return new RecommendationCatalogItem(
                    recommendation.Id,
                    recommendation.ExternalId,
                    displayName,
                    recommendation.Title,
                    city,
                    recommendation.Category,
                    recommendation.Neighborhood,
                    recommendation.Tags,
                    recommendation.PriceLevel,
                    recommendation.Latitude,
                    recommendation.Longitude,
                    recommendation.SuggestedDurationMinutes,
                    recommendation.Rating,
                    recommendation.SourceUrl,
                    periods);
            })
            .OrderBy(item => item.City)
            .ThenBy(item => item.Title)
            .ToList();
    }

    private static IReadOnlyList<ExampleTripRow> CreateExampleRows(
        IReadOnlyList<RecommendationCatalogItem> recommendations)
    {
        var usedRecommendationIds = new HashSet<Guid>();
        var curatedRows = new List<ExampleTripRow>
        {
            CreateExampleRow(
                1,
                "Tokyo",
                "Hotel base Tokyo - Shinjuku",
                "Tarde",
                "Llegada suave: bajar el ritmo, caminar cerca de la base y cerrar con una primera comida facil.",
                recommendations,
                usedRecommendationIds,
                ["cafe", "food", "ramen"],
                false,
                null,
                "Primer dia deliberadamente liviano."),
            CreateExampleRow(
                2,
                "Tokyo",
                "Hotel base Tokyo - Shinjuku",
                "Mañana",
                "Arrancar temprano con cafe/desayuno y un paseo corto antes de que la ciudad se llene.",
                recommendations,
                usedRecommendationIds,
                ["breakfast", "cafe", "market"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                3,
                "Tokyo",
                "Hotel base Tokyo - Shinjuku",
                "Noche",
                "Noche de comida y bares sin alejarse demasiado de las zonas bien conectadas.",
                recommendations,
                usedRecommendationIds,
                ["bar", "nightlife", "dinner", "food"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                5,
                "Tokyo",
                "Hotel base Tokyo - Shinjuku",
                "Tarde",
                "Bloque flexible para compras, cafe o paseo barrial, dejando margen para ajustar por clima.",
                recommendations,
                usedRecommendationIds,
                ["shopping", "cafe", "walk"],
                false,
                null,
                "Si el cliente esta cansado, dejar que Today sugiera algo cercano."),
            CreateExampleRow(
                7,
                "Kyoto",
                "Hotel base Kyoto - Kawaramachi",
                "Tarde",
                "Llegada a Kyoto: paseo contenido y primera lectura del barrio antes de cenar.",
                recommendations,
                usedRecommendationIds,
                ["tea", "walk", "culture", "food"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                8,
                "Kyoto",
                "Hotel base Kyoto - Kawaramachi",
                "Mañana",
                "Mañana tranquila para un punto clasico de Kyoto, evitando cargar el dia de traslados.",
                recommendations,
                usedRecommendationIds,
                ["breakfast", "temple", "market", "cafe"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                9,
                "Kyoto",
                "Hotel base Kyoto - Kawaramachi",
                "Noche",
                "Cena curada en Kyoto; este bloque queda fijo y el resto del dia puede completarse automaticamente.",
                recommendations,
                usedRecommendationIds,
                ["dinner", "sushi", "kaiseki", "food"],
                true,
                "20:00",
                "Reserva de ejemplo, reemplazar por datos reales si existe."),
            CreateExampleRow(
                11,
                "Kyoto",
                "Hotel base Kyoto - Kawaramachi",
                "Tarde",
                "Tarde abierta para cafe, paseo corto o compras suaves segun energia del dia.",
                recommendations,
                usedRecommendationIds,
                ["tea", "cafe", "shopping", "walk"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                13,
                "Osaka",
                "Hotel base Osaka - Namba",
                "Tarde",
                "Llegada a Osaka con foco en ubicarse y no sobrecargar el cambio de ciudad.",
                recommendations,
                usedRecommendationIds,
                ["food", "market", "walk"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                14,
                "Osaka",
                "Hotel base Osaka - Namba",
                "Noche",
                "Noche Osaka: comida informal, luces y plan facil de ajustar si hay cansancio.",
                recommendations,
                usedRecommendationIds,
                ["bar", "nightlife", "dinner", "food"],
                false,
                null,
                string.Empty),
            CreateExampleRow(
                16,
                "Osaka",
                "Hotel base Osaka - Namba",
                "Mañana",
                "Mañana de mercado o desayuno, dejando tarde y noche libres para autofill por cercania.",
                recommendations,
                usedRecommendationIds,
                ["breakfast", "market", "cafe", "food"],
                false,
                null,
                string.Empty)
        };

        var rows = new List<ExampleTripRow>();
        for (var day = 1; day <= 18; day++)
        {
            rows.AddRange(curatedRows.Where(row => row.Day == day));
            if (rows.All(row => row.Day != day))
            {
                var (city, hotel) = ResolveExampleBase(day);
                rows.Add(new ExampleTripRow(
                    day,
                    city,
                    hotel,
                    "Mañana",
                    "autofill",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    false,
                    null,
                    "Dia sin curar: Today deberia completar huecos con la base YUKU."));
            }
        }

        return rows
            .OrderBy(row => row.Day)
            .ThenBy(row => ResolvePeriod(row.Moment)?.DefaultStart ?? TimeOnly.MinValue)
            .ToList();
    }

    private static ExampleTripRow CreateExampleRow(
        int day,
        string city,
        string hotelBase,
        string moment,
        string description,
        IReadOnlyList<RecommendationCatalogItem> recommendations,
        HashSet<Guid> usedRecommendationIds,
        IReadOnlyList<string> preferredKeywords,
        bool isReservation,
        string? time,
        string notes)
    {
        var periodKey = ResolvePeriod(moment)?.Key;
        var locations = recommendations
            .Where(recommendation => string.Equals(recommendation.City, city, StringComparison.OrdinalIgnoreCase))
            .Where(recommendation => !usedRecommendationIds.Contains(recommendation.Id))
            .OrderByDescending(recommendation => periodKey is not null && recommendation.PeriodKeys.Contains(periodKey, StringComparer.OrdinalIgnoreCase))
            .ThenByDescending(recommendation => preferredKeywords.Count(keyword => CreateCatalogSearchText(recommendation).Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(recommendation => recommendation.Rating ?? 0)
            .ThenBy(recommendation => recommendation.Title)
            .Take(3)
            .ToList();

        foreach (var location in locations)
        {
            usedRecommendationIds.Add(location.Id);
        }

        return new ExampleTripRow(
            day,
            city,
            hotelBase,
            moment,
            description,
            locations.ElementAtOrDefault(0)?.DisplayName ?? string.Empty,
            locations.ElementAtOrDefault(1)?.DisplayName ?? string.Empty,
            locations.ElementAtOrDefault(2)?.DisplayName ?? string.Empty,
            isReservation,
            time,
            notes);
    }

    private static (string City, string HotelBase) ResolveExampleBase(int day) =>
        day switch
        {
            <= 6 => ("Tokyo", "Hotel base Tokyo - Shinjuku"),
            <= 12 => ("Kyoto", "Hotel base Kyoto - Kawaramachi"),
            _ => ("Osaka", "Hotel base Osaka - Namba")
        };

    private static string CreateCatalogSearchText(RecommendationCatalogItem recommendation) =>
        NormalizeSearchText(string.Join(
            ' ',
            recommendation.Title,
            recommendation.City,
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.PriceLevel,
            string.Join(' ', recommendation.Tags)));

    private static TripWorkbookImportResult CreateResult(
        TripWorkbookMetadata? metadata,
        IReadOnlyList<TripWorkbookImportRow> rows,
        IReadOnlyList<string> errors,
        bool imported)
    {
        var warningCount = rows.Sum(row => row.Warnings.Count);
        var validRows = rows.Count(row => row.IsValid);
        var autofillRows = rows.Count(row => row.IsAutofill && row.IsValid);
        return new TripWorkbookImportResult(
            imported,
            false,
            false,
            metadata?.TripExternalId,
            metadata?.TravelerName,
            metadata?.DestinationSlug,
            metadata?.StartsOn,
            metadata?.EndsOn,
            rows.Count,
            validRows,
            autofillRows,
            rows.Count(row => !row.IsValid),
            warningCount,
            0,
            0,
            rows,
            errors,
            errors.Count == 0
                ? $"Preview listo. Filas: {rows.Count}; validas: {validRows}; autofill: {autofillRows}; warnings: {warningCount}."
                : $"Preview con errores. Filas: {rows.Count}; errores: {errors.Count}; warnings: {warningCount}.");
    }

    private static void BuildMainSheet(IXLWorksheet sheet)
    {
        sheet.Cell(1, 1).Value = "PIN";
        sheet.Cell(2, 1).Value = "Nombre cliente";
        sheet.Cell(3, 1).Value = "Destino";
        sheet.Cell(4, 1).Value = "Fecha inicio";
        sheet.Cell(5, 1).Value = "Fecha fin";
        sheet.Cell(6, 1).Value = "Timezone";
        sheet.Cell(3, MetadataValueColumn).Value = DefaultDestinationSlug;
        sheet.Cell(6, MetadataValueColumn).Value = DefaultTimezone;

        var metadataRange = sheet.Range(1, 1, 6, 2);
        metadataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        metadataRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        sheet.Range(1, 1, 6, 1).Style.Font.Bold = true;
        sheet.Range(4, MetadataValueColumn, 5, MetadataValueColumn).Style.DateFormat.Format = "yyyy-mm-dd";

        for (var index = 0; index < Headers.Length; index++)
        {
            var cell = sheet.Cell(HeaderRowNumber, index + 1);
            cell.Value = Headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F3ED");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        for (var row = FirstDataRowNumber; row < FirstDataRowNumber + MaxTemplateRows; row++)
        {
            sheet.Cell(row, 2).FormulaA1 = $"=IF(A{row}=\"\",\"\",$B$4+A{row}-1)";
            sheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, HelperListColumn).FormulaA1 =
                $"=IFERROR(\"loc_\"&VLOOKUP($C{row},Validaciones!$E:$F,2,FALSE)&\"_\"&VLOOKUP($E{row},Validaciones!$A:$B,2,FALSE),\"loc_all\")";
        }

        sheet.Columns(1, 12).AdjustToContents();
        sheet.Column(6).Width = 42;
        sheet.Column(7).Width = 36;
        sheet.Column(8).Width = 36;
        sheet.Column(9).Width = 36;
        sheet.Column(12).Width = 34;
        sheet.Column(HelperListColumn).Hide();
        sheet.SheetView.FreezeRows(HeaderRowNumber);
    }

    private static void BuildCatalogSheet(
        IXLWorksheet sheet,
        IReadOnlyList<RecommendationCatalogItem> recommendations)
    {
        var headers = new[]
        {
            "DisplayName",
            "RecommendationId",
            "City",
            "PeriodKeys",
            "Title",
            "Category",
            "Neighborhood",
            "Tags",
            "Rating",
            "PriceLevel",
            "Latitude",
            "Longitude"
        };
        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        for (var index = 0; index < recommendations.Count; index++)
        {
            var recommendation = recommendations[index];
            var row = index + 2;
            sheet.Cell(row, 1).Value = recommendation.DisplayName;
            sheet.Cell(row, 2).Value = recommendation.Id.ToString();
            sheet.Cell(row, 3).Value = recommendation.City;
            sheet.Cell(row, 4).Value = string.Join(", ", recommendation.PeriodKeys);
            sheet.Cell(row, 5).Value = recommendation.Title;
            sheet.Cell(row, 6).Value = recommendation.Category;
            sheet.Cell(row, 7).Value = recommendation.Neighborhood;
            sheet.Cell(row, 8).Value = string.Join(", ", recommendation.Tags);
            if (recommendation.Rating.HasValue)
            {
                sheet.Cell(row, 9).Value = recommendation.Rating.Value;
            }
            sheet.Cell(row, 10).Value = recommendation.PriceLevel;
            sheet.Cell(row, 11).Value = recommendation.Latitude;
            sheet.Cell(row, 12).Value = recommendation.Longitude;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildValidationsSheet(
        IXLWorksheet sheet,
        IReadOnlyList<Destination> destinations,
        IReadOnlyList<RecommendationCatalogItem> recommendations)
    {
        sheet.Cell(1, 1).Value = "Momento";
        sheet.Cell(1, 2).Value = "PeriodKey";
        for (var index = 0; index < TripPeriod.All.Count; index++)
        {
            sheet.Cell(index + 2, 1).Value = TripPeriod.All[index].Label;
            sheet.Cell(index + 2, 2).Value = TripPeriod.All[index].Key;
        }

        sheet.Cell(1, 4).Value = "Destino";
        var destinationValues = destinations.Count > 0
            ? destinations.Select(destination => destination.Slug).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
            : [DefaultDestinationSlug];
        for (var index = 0; index < destinationValues.Count; index++)
        {
            sheet.Cell(index + 2, 4).Value = destinationValues[index];
        }

        sheet.Cell(1, 5).Value = "Ciudad";
        sheet.Cell(1, 6).Value = "CityKey";
        var cities = recommendations
            .Select(recommendation => recommendation.City)
            .Where(city => !string.IsNullOrWhiteSpace(city))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(city => city)
            .DefaultIfEmpty("Tokyo")
            .ToList();
        for (var index = 0; index < cities.Count; index++)
        {
            sheet.Cell(index + 2, 5).Value = cities[index];
            sheet.Cell(index + 2, 6).Value = CreateExcelNameKey(cities[index]);
        }

        sheet.Cell(1, 8).Value = "Reserva";
        sheet.Cell(2, 8).Value = "No";
        sheet.Cell(3, 8).Value = "Si";

        sheet.Columns().AdjustToContents();
    }

    private static void BuildDropdownSheet(
        XLWorkbook workbook,
        IXLWorksheet sheet,
        IReadOnlyList<RecommendationCatalogItem> recommendations)
    {
        var columns = new List<DropdownListColumn>();
        var allItems = recommendations
            .OrderByDescending(item => item.Rating ?? 0)
            .ThenBy(item => item.Title)
            .Select(item => item.DisplayName)
            .ToList();
        columns.Add(new DropdownListColumn("loc_all", allItems));

        var cities = recommendations
            .Select(recommendation => recommendation.City)
            .Where(city => !string.IsNullOrWhiteSpace(city))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(city => city)
            .ToList();

        foreach (var city in cities)
        {
            foreach (var period in TripPeriod.All)
            {
                var name = $"loc_{CreateExcelNameKey(city)}_{period.Key}";
                var items = recommendations
                    .Where(recommendation => string.Equals(recommendation.City, city, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(recommendation => recommendation.PeriodKeys.Contains(period.Key, StringComparer.OrdinalIgnoreCase))
                    .ThenByDescending(recommendation => recommendation.Rating ?? 0)
                    .ThenBy(recommendation => recommendation.Title)
                    .Select(recommendation => recommendation.DisplayName)
                    .ToList();
                columns.Add(new DropdownListColumn(name, items));
            }
        }

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            var excelColumn = columnIndex + 1;
            sheet.Cell(1, excelColumn).Value = column.Name;
            var items = column.Items.Count > 0 ? column.Items : ["-- sin recomendaciones --"];
            for (var rowIndex = 0; rowIndex < items.Count; rowIndex++)
            {
                sheet.Cell(rowIndex + 2, excelColumn).Value = items[rowIndex];
            }

            var range = sheet.Range(2, excelColumn, items.Count + 1, excelColumn);
            workbook.DefinedNames.Add(column.Name, range);
        }

        sheet.Columns().AdjustToContents();
    }

    private static void ApplyMainSheetValidations(
        XLWorkbook workbook,
        IXLWorksheet sheet,
        IXLWorksheet validationsSheet,
        IReadOnlyList<Destination> destinations)
    {
        var momentRange = validationsSheet.Range(2, 1, TripPeriod.All.Count + 1, 1);
        var destinationCount = Math.Max(destinations.Count, 1);
        var destinationRange = validationsSheet.Range(2, 4, destinationCount + 1, 4);
        var cityLastRow = Math.Max(validationsSheet.LastRowUsed()?.RowNumber() ?? 2, 2);
        var cityRange = validationsSheet.Range(2, 5, cityLastRow, 5);
        var reservationRange = validationsSheet.Range(2, 8, 3, 8);

        workbook.DefinedNames.Add("momentos", momentRange);
        workbook.DefinedNames.Add("destinos", destinationRange);
        workbook.DefinedNames.Add("ciudades", cityRange);
        workbook.DefinedNames.Add("reserva_si_no", reservationRange);

        sheet.Cell(3, MetadataValueColumn).CreateDataValidation().List("=destinos");
        sheet.Range(FirstDataRowNumber, 3, FirstDataRowNumber + MaxTemplateRows - 1, 3)
            .CreateDataValidation()
            .List("=ciudades");
        sheet.Range(FirstDataRowNumber, 5, FirstDataRowNumber + MaxTemplateRows - 1, 5)
            .CreateDataValidation()
            .List("=momentos");
        sheet.Range(FirstDataRowNumber, 10, FirstDataRowNumber + MaxTemplateRows - 1, 10)
            .CreateDataValidation()
            .List("=reserva_si_no");

        for (var row = FirstDataRowNumber; row < FirstDataRowNumber + MaxTemplateRows; row++)
        {
            sheet.Range(row, 7, row, 9)
                .CreateDataValidation()
                .List($"=INDIRECT($M{row})");
        }
    }

    private static Dictionary<string, int> CreateHeaderMap(IXLRow headerRow)
    {
        return headerRow.CellsUsed()
            .Select(cell => new
            {
                Header = NormalizeHeader(cell.GetString()),
                Column = cell.Address.ColumnNumber
            })
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Header))
            .GroupBy(cell => cell.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddLegacyHeaderAlias(
        IDictionary<string, int> headerMap,
        string currentHeader,
        string legacyHeader)
    {
        var currentKey = NormalizeHeader(currentHeader);
        var legacyKey = NormalizeHeader(legacyHeader);
        if (!headerMap.ContainsKey(currentKey) && headerMap.TryGetValue(legacyKey, out var column))
        {
            headerMap[currentKey] = column;
        }
    }

    private static bool IsBlankRow(IXLRow row, IReadOnlyDictionary<string, int> headerMap)
    {
        return Headers.All(header =>
            !headerMap.TryGetValue(NormalizeHeader(header), out var column)
            || string.IsNullOrWhiteSpace(row.Cell(column).GetFormattedString()));
    }

    private static string ReadCell(IXLRow row, IReadOnlyDictionary<string, int> headerMap, string header)
    {
        return headerMap.TryGetValue(NormalizeHeader(header), out var column)
            ? row.Cell(column).GetFormattedString().Trim()
            : string.Empty;
    }

    private static string NormalizeOptionalValue(string value)
    {
        var trimmed = value.Trim();
        var normalized = NormalizeSearchText(trimmed);
        return normalized is "" or "-" or "—" or "n a" or "na" or "n/a" or "sin location" or "sin datos"
            ? string.Empty
            : trimmed;
    }

    private static void Require(List<string> errors, int rowNumber, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Fila {rowNumber}: {field} es obligatorio.");
        }
    }

    private static LocationMatch MatchLocation(
        string input,
        string city,
        IReadOnlyDictionary<string, RecommendationCatalogItem> byDisplayName,
        IReadOnlyDictionary<string, List<RecommendationCatalogItem>> byCityAndTitle,
        IReadOnlyDictionary<string, List<RecommendationCatalogItem>> byTitle,
        IReadOnlyDictionary<string, RecommendationCatalogItem> byExternalId,
        List<string> warnings)
    {
        var normalizedInput = NormalizeSearchText(input);
        if (byDisplayName.TryGetValue(normalizedInput, out var displayMatch))
        {
            return LocationMatch.Matched(input, displayMatch);
        }

        if (byExternalId.TryGetValue(normalizedInput, out var externalIdMatch))
        {
            return LocationMatch.Matched(input, externalIdMatch);
        }

        var cityTitleKey = $"{NormalizeSearchText(city)}|{normalizedInput}";
        if (byCityAndTitle.TryGetValue(cityTitleKey, out var cityMatches) && cityMatches.Count == 1)
        {
            return LocationMatch.Matched(input, cityMatches[0]);
        }

        if (byTitle.TryGetValue(normalizedInput, out var titleMatches) && titleMatches.Count == 1)
        {
            var match = titleMatches[0];
            if (!string.IsNullOrWhiteSpace(city)
                && !string.Equals(match.City, city, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"'{input}' matchea una recomendacion en {match.City}, no en {city}.");
            }

            return LocationMatch.Matched(input, match);
        }

        warnings.Add($"'{input}' no matchea una recommendation existente; se importara como evento sin RecommendationId.");
        return LocationMatch.Unmatched(input, "Sin match");
    }

    private static bool ParseReservationFlag(string value, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeSearchText(value);
        if (normalized is "si" or "s" or "yes" or "y" or "true")
        {
            return true;
        }

        if (normalized is "no" or "n" or "false")
        {
            return false;
        }

        warnings.Add($"Reserva '{value}' no se reconoce; se uso No.");
        return false;
    }

    private static TimeOnly? ParseTime(
        IXLRow row,
        IReadOnlyDictionary<string, int> headerMap,
        string header,
        List<string> warnings)
    {
        if (!headerMap.TryGetValue(NormalizeHeader(header), out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.TryGetValue<DateTime>(out var dateTime))
        {
            return TimeOnly.FromDateTime(dateTime);
        }

        var formatted = NormalizeOptionalValue(cell.GetFormattedString());
        if (string.IsNullOrWhiteSpace(formatted))
        {
            return null;
        }

        if (TimeOnly.TryParse(formatted, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariantTime)
            || TimeOnly.TryParse(formatted, new CultureInfo("es-ES"), DateTimeStyles.None, out invariantTime))
        {
            return invariantTime;
        }

        warnings.Add($"Hora '{formatted}' no se pudo parsear; se usara el horario default del momento.");
        return null;
    }

    private static bool TryReadDate(IXLCell cell, out DateOnly date)
    {
        if (cell.TryGetValue<DateTime>(out var dateTime))
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        var formatted = cell.GetFormattedString().Trim();
        if (DateOnly.TryParse(formatted, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(formatted, new CultureInfo("es-ES"), DateTimeStyles.None, out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    private static TripPeriod? ResolvePeriod(string value)
    {
        var normalized = NormalizeSearchText(value);
        return TripPeriod.All.FirstOrDefault(period =>
            NormalizeSearchText(period.Label) == normalized
            || period.Aliases.Any(alias => NormalizeSearchText(alias) == normalized));
    }

    private static bool IsExplicitFreeBlock(string value)
    {
        var normalized = NormalizeSearchText(value);
        return normalized is "libre" or "autofill" or "auto" or "completar automaticamente" or "completar automatico";
    }

    private static IReadOnlyList<string> ResolvePeriodKeys(Recommendation recommendation)
    {
        var text = NormalizeSearchText(CreateRecommendationSearchText(recommendation));
        var periodKeys = TripPeriod.All
            .Where(period => period.Keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
            .Select(period => period.Key)
            .ToList();

        if (periodKeys.Count == 0 && text.Contains("food", StringComparison.Ordinal))
        {
            periodKeys.Add("midday");
            periodKeys.Add("night");
        }

        return periodKeys.Count == 0
            ? TripPeriod.All.Select(period => period.Key).ToList()
            : periodKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string CreateRecommendationSearchText(Recommendation recommendation) =>
        string.Join(
            ' ',
            recommendation.Title,
            recommendation.Category,
            recommendation.Neighborhood,
            recommendation.Description,
            recommendation.PriceLevel,
            string.Join(' ', recommendation.Tags),
            recommendation.CurationNotes);

    private static string ResolveRecommendationCity(Recommendation recommendation)
    {
        if (!string.IsNullOrWhiteSpace(recommendation.Neighborhood))
        {
            var city = recommendation.Neighborhood.Split(',', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(city))
            {
                return city;
            }
        }

        return "Unknown City";
    }

    private static string CreateDisplayName(Recommendation recommendation, bool includeExternalId)
    {
        var city = ResolveRecommendationCity(recommendation);
        var parts = new List<string> { recommendation.Title, city, recommendation.Category };
        if (includeExternalId && !string.IsNullOrWhiteSpace(recommendation.ExternalId))
        {
            parts.Add(recommendation.ExternalId);
        }

        return string.Join(" - ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string CreateReservationTitle(TripWorkbookRowDraft row, LocationMatch match)
    {
        var title = match.Recommendation?.Title ?? match.Input;
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"Plan {row.Period.Label}";
        }

        return row.IsReservation ? $"Reserva - {title}" : title;
    }

    private static string CreateReservationNotes(string description, string notes)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add($"Descripcion: {description.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            parts.Add($"Notas: {notes.Trim()}");
        }

        return string.Join(" | ", parts);
    }

    private static string SourceSlug(TripWorkbookMetadata metadata) =>
        $"trip-excel-{metadata.TripExternalId}";

    private static string CreateTripExternalId(
        string destinationSlug,
        string pin,
        DateOnly startsOn,
        DateOnly endsOn) =>
        TrimToMax($"trip-{Slugify(destinationSlug)}-{pin}-{startsOn:yyyyMMdd}-{endsOn:yyyyMMdd}", 160);

    private static string CreateTripUserEmail(TripWorkbookMetadata metadata)
    {
        var localPart = TrimToMax(
            $"trip-{Slugify(metadata.DestinationSlug)}-{metadata.Pin}-{metadata.StartsOn:yyyyMMdd}",
            120);
        return $"{localPart}@travelcompanion.local";
    }

    private static string NormalizeHeader(string value)
    {
        return NormalizeSearchText(value);
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var noDiacritics = RemoveDiacritics(value)
            .Replace('/', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace('-', ' ');
        return WhitespaceRegex().Replace(noDiacritics, " ").Trim().ToLowerInvariant();
    }

    private static string Slugify(string value)
    {
        var normalized = NormalizeSearchText(value);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string CreateExcelNameKey(string value)
    {
        var slug = Slugify(value).Replace('-', '_');
        return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string TrimToMax(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record ParsedTripWorkbook(
        TripWorkbookMetadata? Metadata,
        IReadOnlyList<TripWorkbookRowDraft> Rows,
        TripWorkbookImportResult Result);

    private sealed record TripWorkbookMetadata(
        string Pin,
        string TravelerName,
        Guid DestinationId,
        string DestinationSlug,
        DateOnly StartsOn,
        DateOnly EndsOn,
        string TimeZoneId,
        string TripExternalId);

    private sealed record TripWorkbookRowDraft(
        int RowNumber,
        int Day,
        DateOnly Date,
        string City,
        string HotelBase,
        TripPeriod Period,
        string CuratedDescription,
        IReadOnlyList<LocationMatch> LocationMatches,
        bool IsReservation,
        TimeOnly? StartTime,
        string Notes,
        bool IsAutofill);

    private sealed record RecommendationCatalogItem(
        Guid Id,
        string? ExternalId,
        string DisplayName,
        string Title,
        string City,
        string Category,
        string Neighborhood,
        IReadOnlyList<string> Tags,
        string PriceLevel,
        decimal Latitude,
        decimal Longitude,
        int SuggestedDurationMinutes,
        double? Rating,
        string? SourceUrl,
        IReadOnlyList<string> PeriodKeys);

    private sealed record LocationMatch(
        string Input,
        RecommendationCatalogItem? Recommendation,
        string Status)
    {
        public static LocationMatch Matched(string input, RecommendationCatalogItem recommendation) =>
            new(input, recommendation, "OK");

        public static LocationMatch Unmatched(string input, string status) =>
            new(input, null, status);

        public TripWorkbookLocationMatch ToPreview() =>
            new(Input, Recommendation?.Id, Recommendation?.Title, Recommendation?.City, Status);
    }

    private sealed record DropdownListColumn(string Name, IReadOnlyList<string> Items);

    private sealed record ExampleTripRow(
        int Day,
        string City,
        string HotelBase,
        string Moment,
        string CuratedDescription,
        string Location1,
        string Location2,
        string Location3,
        bool IsReservation,
        string? Time,
        string Notes);

    private sealed record TripPeriod(
        string Key,
        string Label,
        TimeOnly DefaultStart,
        int DefaultDurationMinutes,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> Keywords)
    {
        public static IReadOnlyList<TripPeriod> All { get; } =
        [
            new("morning", "Mañana", new TimeOnly(9, 0), 75, ["manana", "morning"], ["coffee", "cafe", "breakfast", "desayuno", "temple", "shrine", "market", "walk", "culture", "garden"]),
            new("midday", "Medio día", new TimeOnly(12, 30), 75, ["medio dia", "mediodia", "almuerzo", "lunch"], ["food", "lunch", "almuerzo", "ramen", "sushi", "restaurant", "shopping", "market", "brunch"]),
            new("afternoon", "Tarde", new TimeOnly(16, 0), 90, ["afternoon"], ["walk", "culture", "shopping", "museum", "garden", "route", "tea", "cafe"]),
            new("night", "Noche", new TimeOnly(20, 0), 90, ["night", "cena", "dinner"], ["dinner", "cena", "bar", "night", "izakaya", "food", "view", "dance", "nightlife"])
        ];
    }
}

public sealed record TripWorkbookImportResult(
    bool Imported,
    bool CreatedTrip,
    bool UpdatedTrip,
    string? TripExternalId,
    string? TravelerName,
    string? DestinationSlug,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int TotalRows,
    int ValidRows,
    int AutofillRows,
    int ErrorRows,
    int WarningCount,
    int CreatedReservations,
    int CreatedLodgingReservations,
    IReadOnlyList<TripWorkbookImportRow> Rows,
    IReadOnlyList<string> Errors,
    string StatusMessage)
{
    public bool HasRows => Rows.Count > 0;
    public bool HasErrors => Errors.Count > 0;
    public bool CanImport => !HasErrors && TripExternalId is not null;
}

public sealed record TripWorkbookImportRow(
    int RowNumber,
    int Day,
    DateOnly Date,
    string City,
    string HotelBase,
    string Moment,
    string CuratedDescription,
    IReadOnlyList<string> LocationInputs,
    IReadOnlyList<TripWorkbookLocationMatch> LocationMatches,
    bool IsReservation,
    string? Time,
    string Notes,
    bool IsAutofill,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
    public int MatchedLocationCount => LocationMatches.Count(match => match.RecommendationId.HasValue);
}

public sealed record TripWorkbookLocationMatch(
    string Input,
    Guid? RecommendationId,
    string? Title,
    string? City,
    string Status);
