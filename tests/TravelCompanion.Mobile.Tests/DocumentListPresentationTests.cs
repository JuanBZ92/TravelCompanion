using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Tests;

public sealed class DocumentListPresentationTests
{
    [Fact]
    public void Personal_categories_precede_included_sections_and_keep_row_order_and_actions()
    {
        var first = new DocumentRow("Billete", () => { });
        var second = new DocumentRow("Reserva", () => { });
        var firstCategory = new DocumentListGroup("Transporte", [first]);
        var secondCategory = new DocumentListGroup("Alojamiento", [second]);
        var flights = new DocumentListGroup("Vuelos", [new object(), new object(), new object()]);
        var hotels = new DocumentListGroup("Hoteles · Confirmaciones", [new object()]);
        var other = new DocumentListGroup("Otros", [new object()]);
        var stays = new DocumentListGroup("Hoteles", [new object()]);

        var result = DocumentListPresentation.Build([firstCategory, secondCategory],
            [flights, hotels, other, stays], false, true, true);

        Assert.Equal(["Transporte", "Alojamiento", "Vuelos", "Hoteles · Confirmaciones", "Otros", "Hoteles"],
            result.Select(group => group.Name));
        Assert.Same(firstCategory, result[0]);
        Assert.Same(secondCategory, result[1]);
        Assert.Same(first, result[0][0]);
        Assert.Same(second.Open, ((DocumentRow)result[1][0]).Open);
        Assert.Equal(flights.ToArray(), result[2].ToArray());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Category_filter_or_missing_included_permission_preserves_only_personal_documents(
        bool categorySelected, bool hasCuratedDocuments, bool hasKnownValidAccess)
    {
        var personal = new DocumentListGroup("Documentos personales", [new object()]);

        var result = DocumentListPresentation.Build([personal], ForbiddenIncludedEnumeration(),
            categorySelected, hasCuratedDocuments, hasKnownValidAccess);

        Assert.Same(personal, Assert.Single(result));
    }

    [Fact]
    public void Empty_groups_do_not_create_blank_categories()
    {
        var personal = new DocumentListGroup("Sin documentos", []);
        var included = new DocumentListGroup("Sin hoteles", []);

        Assert.Empty(DocumentListPresentation.Build([personal], [included], false, true, true));
    }

    [Fact]
    public void Large_personal_and_included_lists_have_no_row_limit()
    {
        var personalRows = Enumerable.Range(0, 250).Select(index => (object)new DocumentRow($"Personal {index}", () => { })).ToArray();
        var includedRows = Enumerable.Range(0, 250).Select(index => (object)new DocumentRow($"Incluido {index}", () => { })).ToArray();
        var personal = new DocumentListGroup("Otros personales", personalRows);
        var included = new DocumentListGroup("Otros incluidos", includedRows);

        var result = DocumentListPresentation.Build([personal], [included], false, true, true);

        Assert.Equal(500, result.Sum(group => group.Count));
        Assert.Equal(personalRows, result[0].ToArray());
        Assert.Equal(includedRows, result[1].ToArray());
    }

    [Fact]
    public void Flight_summary_selectors_and_each_selected_leg_remain_individual_rows()
    {
        var summary = new object();
        var outbound = new object();
        var inbound = new object();
        var legs = Enumerable.Range(0, 100).Select(_ => new object()).ToArray();
        var flights = new DocumentListGroup("Vuelos", new[] { summary, outbound, inbound }.Concat(legs));

        var result = DocumentListPresentation.Build([], [flights], false, true, true);

        var group = Assert.Single(result);
        Assert.Equal(103, group.Count);
        Assert.Same(summary, group[0]);
        Assert.Same(outbound, group[1]);
        Assert.Same(inbound, group[2]);
        Assert.Equal(legs, group.Skip(3));
    }

    [Fact]
    public void Regrouping_after_move_or_delete_publishes_only_the_current_rows()
    {
        var moved = new object();
        var retained = new object();
        var previous = new DocumentListGroup("Transporte", [moved, retained]);
        var before = DocumentListPresentation.Build([previous], [], false, false, true);
        var after = DocumentListPresentation.Build([new("Transporte", [retained]), new("Alojamiento", [moved])],
            [], false, false, true);
        var deleted = DocumentListPresentation.Build([new("Transporte", [retained]), new("Alojamiento", [])],
            [], false, false, true);

        Assert.Equal(2, Assert.Single(before).Count);
        Assert.Same(moved, after[1][0]);
        Assert.Same(retained, Assert.Single(deleted)[0]);
    }

    private static IEnumerable<DocumentListGroup> ForbiddenIncludedEnumeration() =>
        Enumerable.Range(0, 1).Select<int, DocumentListGroup>(_ =>
            throw new InvalidOperationException("Included documents must not be enumerated without access."));

    private sealed record DocumentRow(string Title, Action Open);
}
