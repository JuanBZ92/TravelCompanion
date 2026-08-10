using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Api.Pages.Admin;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class AdminWorkbookImportPageTests
{
    [Fact]
    public async Task Yuku_preview_replaces_the_initial_empty_hidden_field_value()
    {
        await using var dbContext = CreateDbContext();
        var page = new YukuJapanImportModel(new YukuJapanRecommendationImportService(
            dbContext,
            NullLogger<YukuJapanRecommendationImportService>.Instance));
        ConfigurePage(page, CreateWorkbook());

        await page.OnPostPreviewAsync();

        Assert.False(page.ModelState.ContainsKey(nameof(page.PreviewWorkbookBase64)));
        Assert.False(string.IsNullOrWhiteSpace(page.PreviewWorkbookBase64));
    }

    [Fact]
    public async Task Trip_preview_replaces_the_initial_empty_hidden_field_value()
    {
        await using var dbContext = CreateDbContext();
        var page = new TripImportModel(new TripWorkbookImportService(
            dbContext,
            new PasswordHasher<Trip>(),
            NullLogger<TripWorkbookImportService>.Instance));
        ConfigurePage(page, CreateWorkbook());

        await page.OnPostPreviewAsync();

        Assert.False(page.ModelState.ContainsKey(nameof(page.PreviewWorkbookBase64)));
        Assert.False(string.IsNullOrWhiteSpace(page.PreviewWorkbookBase64));
    }

    private static void ConfigurePage(YukuJapanImportModel page, byte[] workbookBytes)
    {
        page.PageContext = CreatePageContext();
        page.Workbook = CreateFormFile(workbookBytes);
        page.ModelState.SetModelValue(
            nameof(page.PreviewWorkbookBase64),
            new ValueProviderResult(string.Empty));
    }

    private static void ConfigurePage(TripImportModel page, byte[] workbookBytes)
    {
        page.PageContext = CreatePageContext();
        page.Workbook = CreateFormFile(workbookBytes);
        page.ModelState.SetModelValue(
            nameof(page.PreviewWorkbookBase64),
            new ValueProviderResult(string.Empty));
    }

    private static PageContext CreatePageContext() => new()
    {
        HttpContext = new DefaultHttpContext()
    };

    private static FormFile CreateFormFile(byte[] workbookBytes) => new(
        new MemoryStream(workbookBytes),
        0,
        workbookBytes.Length,
        "Workbook",
        "workbook.xlsx");

    private static byte[] CreateWorkbook()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Sheet1").Cell("A1").Value = "Test";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static TravelCompanionDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TravelCompanionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TravelCompanionDbContext(options);
    }
}
