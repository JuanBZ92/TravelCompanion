using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed partial class YukuJapanRecommendationImportService(
    TravelCompanionDbContext dbContext,
    ILogger<YukuJapanRecommendationImportService> logger)
{
    private const string DestinationSlug = "japon";
    private const string SourceName = "YUKU Japan verificada v1";

    private static readonly string[] RequiredHeaders =
    [
        "Ciudad",
        "Lugar",
        "Comentario",
        "Tipo de comida",
        "Google Maps Link",
        "Coordenadas Value",
        "Tabelog Score (Numérico - Ordenar)",
        "Reserva",
        "Precio Aprox"
    ];

    public async Task<YukuJapanRecommendationImportResult> PreviewAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        var parseResult = await ParseAsync(workbookStream, cancellationToken).ConfigureAwait(false);
        return parseResult.Result;
    }

    public async Task<YukuJapanRecommendationImportResult> ImportAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default)
    {
        var parseResult = await ParseAsync(workbookStream, cancellationToken).ConfigureAwait(false);
        var result = parseResult.Result;
        if (result.HasErrors)
        {
            return result with
            {
                StatusMessage = "No se importo nada porque el archivo tiene errores."
            };
        }

        var destination = await dbContext.Destinations
            .FirstOrDefaultAsync(existingDestination => existingDestination.Slug == DestinationSlug, cancellationToken)
            .ConfigureAwait(false);
        if (destination is null)
        {
            return result with
            {
                Errors = [.. result.Errors, $"No existe el destino con slug '{DestinationSlug}'."],
                StatusMessage = "No se importo nada porque falta el destino Japon."
            };
        }

        var externalIds = parseResult.Rows
            .Select(row => row.ExternalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existingRecommendations = await dbContext.Recommendations
            .Include(recommendation => recommendation.Packages)
            .Where(recommendation => recommendation.DestinationId == destination.Id
                && recommendation.ExternalId != null
                && externalIds.Contains(recommendation.ExternalId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingByExternalId = existingRecommendations
            .ToDictionary(recommendation => recommendation.ExternalId!, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var updated = 0;
        foreach (var row in parseResult.Rows)
        {
            if (!existingByExternalId.TryGetValue(row.ExternalId, out var recommendation))
            {
                recommendation = new Recommendation
                {
                    Id = Guid.NewGuid(),
                    DestinationId = destination.Id,
                    Title = string.Empty,
                    Category = string.Empty,
                    Neighborhood = string.Empty,
                    Description = string.Empty
                };
                dbContext.Recommendations.Add(recommendation);
                existingByExternalId[row.ExternalId] = recommendation;
                created++;
            }
            else
            {
                updated++;
            }

            ApplyRow(recommendation, destination.Id, row);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "YUKU Japan recommendation import complete. Created={Created}; Updated={Updated}; Rows={Rows}.",
            created,
            updated,
            parseResult.Rows.Count);

        return result with
        {
            Imported = true,
            CreatedCount = created,
            UpdatedCount = updated,
            StatusMessage = $"Import completo. Creadas: {created}; actualizadas: {updated}; warnings: {result.WarningCount}."
        };
    }

    private async Task<ParsedYukuJapanWorkbook> ParseAsync(
        Stream workbookStream,
        CancellationToken cancellationToken)
    {
        var destinationExists = await dbContext.Destinations
            .AsNoTracking()
            .AnyAsync(destination => destination.Slug == DestinationSlug, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<YukuJapanRecommendationImportRow>();
        var drafts = new List<YukuJapanRecommendationDraft>();
        var errors = new List<string>();
        var seenExternalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var workbook = new XLWorkbook(workbookStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            return new ParsedYukuJapanWorkbook([], CreateResult([], ["El archivo no tiene hojas."], imported: false));
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            return new ParsedYukuJapanWorkbook([], CreateResult([], ["La primera hoja esta vacia."], imported: false));
        }

        var headerMap = CreateHeaderMap(usedRange.FirstRowUsed());
        foreach (var requiredHeader in RequiredHeaders)
        {
            if (!headerMap.ContainsKey(NormalizeHeader(requiredHeader)))
            {
                errors.Add($"Falta la columna obligatoria '{requiredHeader}'.");
            }
        }

        if (!destinationExists)
        {
            errors.Add($"No existe el destino con slug '{DestinationSlug}'.");
        }

        if (errors.Count > 0)
        {
            return new ParsedYukuJapanWorkbook([], CreateResult(rows, errors, imported: false));
        }

        foreach (var excelRow in usedRange.RowsUsed().Skip(1))
        {
            var rowNumber = excelRow.RowNumber();
            if (IsBlankRow(excelRow))
            {
                continue;
            }

            var rowErrors = new List<string>();
            var rowWarnings = new List<string>();
            var city = ReadCell(excelRow, headerMap, "Ciudad");
            var title = ReadCell(excelRow, headerMap, "Lugar");
            var comment = ReadCell(excelRow, headerMap, "Comentario");
            var foodType = ReadCell(excelRow, headerMap, "Tipo de comida");
            var mapsLink = ReadCell(excelRow, headerMap, "Google Maps Link");
            var coordinates = ReadCell(excelRow, headerMap, "Coordenadas Value");
            var reservation = ReadCell(excelRow, headerMap, "Reserva");
            var approximatePrice = ReadCell(excelRow, headerMap, "Precio Aprox");

            Require(rowErrors, rowNumber, "Ciudad", city);
            Require(rowErrors, rowNumber, "Lugar", title);
            Require(rowErrors, rowNumber, "Comentario", comment);
            Require(rowErrors, rowNumber, "Tipo de comida", foodType);
            Require(rowErrors, rowNumber, "Google Maps Link", mapsLink);
            Require(rowErrors, rowNumber, "Coordenadas Value", coordinates);

            if (string.IsNullOrWhiteSpace(reservation))
            {
                rowWarnings.Add("Reserva vacia.");
            }

            var priceLevel = InferPriceLevel(approximatePrice, rowWarnings);
            var (latitude, longitude) = ParseCoordinates(coordinates);
            if (!latitude.HasValue || !longitude.HasValue)
            {
                rowErrors.Add($"Fila {rowNumber}: Coordenadas Value debe tener formato 'lat, lon'.");
            }

            var externalId = CreateExternalId(city, title);
            if (!seenExternalIds.Add(externalId))
            {
                rowErrors.Add($"Fila {rowNumber}: external_id duplicado '{externalId}' dentro del archivo.");
            }

            var category = InferCategory(foodType);
            var tags = InferTags(foodType, reservation, priceLevel, title, comment);
            var duration = InferDurationMinutes(foodType);
            var rating = ParseRating(excelRow, headerMap);
            var curationNotes = CreateCurationNotes(foodType, reservation, approximatePrice);

            var previewRow = new YukuJapanRecommendationImportRow(
                rowNumber,
                externalId,
                title,
                city,
                category,
                $"{city}, Japan",
                priceLevel,
                duration,
                tags,
                latitude,
                longitude,
                rating,
                rowErrors,
                rowWarnings);
            rows.Add(previewRow);
            errors.AddRange(rowErrors);

            if (rowErrors.Count == 0)
            {
                drafts.Add(new YukuJapanRecommendationDraft(
                    externalId,
                    title.Trim(),
                    category,
                    $"{city.Trim()}, Japan",
                    comment.Trim(),
                    tags,
                    priceLevel,
                    latitude!.Value,
                    longitude!.Value,
                    duration,
                    rating,
                    SourceName,
                    mapsLink.Trim(),
                    curationNotes));
            }
        }

        return new ParsedYukuJapanWorkbook(drafts, CreateResult(rows, errors, imported: false));
    }

    private static YukuJapanRecommendationImportResult CreateResult(
        IReadOnlyList<YukuJapanRecommendationImportRow> rows,
        IReadOnlyList<string> errors,
        bool imported)
    {
        var warningCount = rows.Sum(row => row.Warnings.Count);
        return new YukuJapanRecommendationImportResult(
            imported,
            rows.Count,
            rows.Count(row => row.IsValid),
            rows.Count(row => !row.IsValid),
            warningCount,
            0,
            0,
            rows,
            errors,
            errors.Count == 0
                ? $"Preview listo. Filas: {rows.Count}; validas: {rows.Count(row => row.IsValid)}; warnings: {warningCount}."
                : $"Preview con errores. Filas: {rows.Count}; errores: {errors.Count}; warnings: {warningCount}.");
    }

    private static Dictionary<string, int> CreateHeaderMap(IXLRangeRow headerRow)
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

    private static bool IsBlankRow(IXLRangeRow row)
    {
        return row.CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString()));
    }

    private static string ReadCell(IXLRangeRow row, IReadOnlyDictionary<string, int> headerMap, string header)
    {
        return headerMap.TryGetValue(NormalizeHeader(header), out var column)
            ? row.Cell(column).GetFormattedString().Trim()
            : string.Empty;
    }

    private static void Require(List<string> errors, int rowNumber, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Fila {rowNumber}: {field} es obligatorio.");
        }
    }

    private static void ApplyRow(Recommendation recommendation, Guid destinationId, YukuJapanRecommendationDraft row)
    {
        recommendation.ExternalId = row.ExternalId;
        recommendation.DestinationId = destinationId;
        recommendation.Title = row.Title;
        recommendation.Category = row.Category;
        recommendation.Neighborhood = row.Neighborhood;
        recommendation.Description = row.Description;
        recommendation.Tags = row.Tags.ToList();
        recommendation.PriceLevel = row.PriceLevel;
        recommendation.Latitude = row.Latitude;
        recommendation.Longitude = row.Longitude;
        recommendation.SuggestedDurationMinutes = row.SuggestedDurationMinutes;
        recommendation.Rating = row.Rating;
        recommendation.OpeningHours = null;
        recommendation.SourceName = row.SourceName;
        recommendation.SourceUrl = row.SourceUrl;
        recommendation.CurationNotes = row.CurationNotes;
        recommendation.AccessLevel = ContentAccessLevel.Free;
        recommendation.Packages.Clear();
    }

    private static string InferCategory(string foodType)
    {
        var normalized = NormalizeSearchText(foodType);
        return normalized.Contains("bar", StringComparison.Ordinal)
            ? "Nightlife"
            : "Food";
    }

    private static int InferDurationMinutes(string foodType)
    {
        var normalized = NormalizeSearchText(foodType);
        if (ContainsAny(normalized, "cafe", "desayuno"))
        {
            return 45;
        }

        if (ContainsAny(normalized, "ramen", "gyoza", "udon", "donburi", "tonkatsu"))
        {
            return 60;
        }

        if (ContainsAny(normalized, "sushi kaiten", "standing", "soba", "tempura", "tendon", "pizza", "burger", "hamburguesa", "mercado", "food hall"))
        {
            return 75;
        }

        if (ContainsAny(normalized, "bar", "izakaya", "yakitori", "yakiniku", "unagi", "fusion"))
        {
            return 90;
        }

        if (ContainsAny(normalized, "kaiseki", "kappo", "edomae", "experiencia", "tienda especial"))
        {
            return 120;
        }

        return 75;
    }

    private static IReadOnlyList<string> InferTags(
        string foodType,
        string reservation,
        string priceLevel,
        string title,
        string comment)
    {
        var tags = new List<string>();
        AddTag(tags, "food");

        var normalizedType = NormalizeSearchText(foodType);
        var normalizedReservation = NormalizeSearchText(reservation);
        var searchableText = NormalizeSearchText($"{foodType} {title} {comment}");

        if (ContainsAny(normalizedType, "bar"))
        {
            AddTag(tags, "nightlife");
            AddTag(tags, "bar");
        }

        AddIfContains(tags, searchableText, "sushi", "sushi");
        AddIfContains(tags, searchableText, "ramen", "ramen");
        AddIfContains(tags, searchableText, "cafe", "cafe");
        AddIfContains(tags, searchableText, "desayuno", "breakfast");
        AddIfContains(tags, searchableText, "tempura", "tempura");
        AddIfContains(tags, searchableText, "tendon", "tempura");
        AddIfContains(tags, searchableText, "soba", "soba");
        AddIfContains(tags, searchableText, "udon", "udon");
        AddIfContains(tags, searchableText, "gyoza", "gyoza");
        AddIfContains(tags, searchableText, "yakiniku", "yakiniku");
        AddIfContains(tags, searchableText, "yakitori", "yakitori");
        AddIfContains(tags, searchableText, "tonkatsu", "tonkatsu");
        AddIfContains(tags, searchableText, "unagi", "unagi");
        AddIfContains(tags, searchableText, "anguila", "unagi");
        AddIfContains(tags, searchableText, "kaiseki", "kaiseki");
        AddIfContains(tags, searchableText, "kappo", "kaiseki");
        AddIfContains(tags, searchableText, "pizza", "pizza");
        AddIfContains(tags, searchableText, "burger", "burger");
        AddIfContains(tags, searchableText, "hamburguesa", "burger");
        AddIfContains(tags, searchableText, "mercado", "market");
        AddIfContains(tags, searchableText, "food hall", "market");
        AddIfContains(tags, searchableText, "tea", "tea");

        if (ContainsAny(normalizedReservation, "walk in", "walk-in"))
        {
            AddTag(tags, "walk-in");
        }

        if (ContainsAny(normalizedReservation, "solo con reserva", "reservar"))
        {
            AddTag(tags, "reservation required");
        }
        else if (ContainsAny(normalizedReservation, "recomendada", "reserva recomendada", "disponible", "reservas disponibles"))
        {
            AddTag(tags, "reservation recommended");
        }

        if (string.Equals(priceLevel, "high", StringComparison.OrdinalIgnoreCase))
        {
            AddTag(tags, "premium");
        }

        return tags;
    }

    private static string InferPriceLevel(string approximatePrice, List<string> warnings)
    {
        var values = NumberRegex()
            .Matches(approximatePrice)
            .Select(match => match.Value.Replace(",", string.Empty).Replace(".", string.Empty))
            .Where(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .ToList();

        if (values.Count == 0)
        {
            warnings.Add("Precio Aprox vacio o no parseable; se uso medium.");
            return "medium";
        }

        var maxPrice = values.Max();
        return maxPrice <= 2500
            ? "low"
            : maxPrice <= 7000
                ? "medium"
                : "high";
    }

    private static (decimal? Latitude, decimal? Longitude) ParseCoordinates(string coordinates)
    {
        var match = CoordinatesRegex().Match(coordinates);
        if (!match.Success)
        {
            return (null, null);
        }

        return decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var latitude)
            && decimal.TryParse(match.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var longitude)
            ? (latitude, longitude)
            : (null, null);
    }

    private static double? ParseRating(IXLRangeRow row, IReadOnlyDictionary<string, int> headerMap)
    {
        if (!headerMap.TryGetValue(NormalizeHeader("Tabelog Score (Numérico - Ordenar)"), out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.TryGetValue<double>(out var rating))
        {
            return rating is >= 0 and <= 5 ? rating : null;
        }

        return double.TryParse(cell.GetFormattedString(), NumberStyles.Float, CultureInfo.InvariantCulture, out rating)
            && rating is >= 0 and <= 5
            ? rating
            : null;
    }

    private static string CreateCurationNotes(string foodType, string reservation, string approximatePrice)
    {
        var notes = new List<string>();
        AddNote(notes, "Tipo de comida", foodType);
        AddNote(notes, "Reserva", reservation);
        AddNote(notes, "Precio aprox", approximatePrice);
        return string.Join(" | ", notes);
    }

    private static void AddNote(List<string> notes, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            notes.Add($"{label}: {value.Trim()}");
        }
    }

    private static string CreateExternalId(string city, string title)
    {
        return $"yuku-japan-{Slugify(city)}-{Slugify(title)}";
    }

    private static string Slugify(string value)
    {
        var normalized = RemoveDiacritics(value).ToLowerInvariant();
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

    private static void AddIfContains(List<string> tags, string value, string needle, string tag)
    {
        if (value.Contains(needle, StringComparison.Ordinal))
        {
            AddTag(tags, tag);
        }
    }

    private static void AddTag(List<string> tags, string tag)
    {
        if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(tag);
        }
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)")]
    private static partial Regex CoordinatesRegex();

    [GeneratedRegex(@"\d[\d,.]*")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record ParsedYukuJapanWorkbook(
        IReadOnlyList<YukuJapanRecommendationDraft> Rows,
        YukuJapanRecommendationImportResult Result);

    private sealed record YukuJapanRecommendationDraft(
        string ExternalId,
        string Title,
        string Category,
        string Neighborhood,
        string Description,
        IReadOnlyList<string> Tags,
        string PriceLevel,
        decimal Latitude,
        decimal Longitude,
        int SuggestedDurationMinutes,
        double? Rating,
        string SourceName,
        string SourceUrl,
        string CurationNotes);
}

public sealed record YukuJapanRecommendationImportResult(
    bool Imported,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int WarningCount,
    int CreatedCount,
    int UpdatedCount,
    IReadOnlyList<YukuJapanRecommendationImportRow> Rows,
    IReadOnlyList<string> Errors,
    string StatusMessage)
{
    public bool HasRows => Rows.Count > 0;
    public bool HasErrors => Errors.Count > 0;
    public bool CanImport => HasRows && !HasErrors;
}

public sealed record YukuJapanRecommendationImportRow(
    int RowNumber,
    string ExternalId,
    string Title,
    string City,
    string Category,
    string Neighborhood,
    string PriceLevel,
    int SuggestedDurationMinutes,
    IReadOnlyList<string> Tags,
    decimal? Latitude,
    decimal? Longitude,
    double? Rating,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
