using System.Collections.ObjectModel;

namespace TravelCompanion.Mobile.ViewModels;

// A group contains individual rows. The CollectionView virtualizes each row,
// including flight legs; a document group is never an eagerly created layout.
public sealed class DocumentListGroup(string name, IEnumerable<object> rows) : ObservableCollection<object>(rows)
{
    public string Name { get; } = name;
}

public static class DocumentListPresentation
{
    public static IReadOnlyList<DocumentListGroup> Build(
        IEnumerable<DocumentListGroup> personal,
        IEnumerable<DocumentListGroup> included,
        bool categorySelected,
        bool hasCuratedDocuments,
        bool hasKnownValidAccess)
    {
        var groups = personal.Where(group => group.Count > 0).ToList();
        if (!categorySelected && hasCuratedDocuments && hasKnownValidAccess)
            groups.AddRange(included.Where(group => group.Count > 0));
        return groups;
    }
}
