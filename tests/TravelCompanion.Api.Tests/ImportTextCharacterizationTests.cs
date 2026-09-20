using System.Reflection;
using ClosedXML.Excel;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class ImportTextCharacterizationTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" \t\n ", "")]
    [InlineData("  CAFÉ / (Templo)-東京  ", "cafe templo 東京")]
    [InlineData("Cafe\u0301", "cafe")]
    [InlineData("  Niño\tÁRBOL\n ", "nino arbol")]
    public void Both_importers_preserve_header_normalization(string? value, string expected)
    {
        foreach (var importer in Importers)
            Assert.Equal(expected, Call(importer, "NormalizeHeader", value));
    }

    [Fact]
    public void Both_row_adapters_keep_first_duplicate_header_and_trim_cells()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Data");
        sheet.Cell(1, 1).Value = "Café";
        sheet.Cell(1, 2).Value = "CAFE\u0301";
        sheet.Cell(1, 3).Value = "Ciudad";
        sheet.Cell(1, 4).Value = "Vacío";
        sheet.Cell(2, 1).Value = "  First  ";
        sheet.Cell(2, 2).Value = "Second";
        sheet.Cell(2, 3).Value = " 東京 ";
        foreach (var importer in Importers)
        {
            object header = importer == typeof(TripWorkbookImportService) ? sheet.Row(1) : sheet.Range("A1:D2").Row(1);
            object row = importer == typeof(TripWorkbookImportService) ? sheet.Row(2) : sheet.Range("A1:D2").Row(2);
            var map = (Dictionary<string, int>)Call(importer, "CreateHeaderMap", header)!;
            Assert.Equal(3, map.Count);
            Assert.Equal(1, map["cafe"]);
            Assert.Equal("First", Call(importer, "ReadCell", row, map, " CAFÉ "));
            Assert.Equal("東京", Call(importer, "ReadCell", row, map, "Ciudad"));
            Assert.Equal("", Call(importer, "ReadCell", row, map, "Vacío"));
            Assert.Equal("", Call(importer, "ReadCell", row, map, "Missing"));
        }
    }

    private static readonly Type[] Importers = [typeof(TripWorkbookImportService), typeof(YukuJapanRecommendationImportService)];

    private static object? Call(Type type, string method, params object?[] arguments) =>
        type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
}
