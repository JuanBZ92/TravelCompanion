using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public class DocumentFolderTests
{
    [Fact]
    public void CategoryFilterKeepsLegacyFilesInOtherAndExcludesIncludedDocuments()
    {
        var legacy = new LocalTripDocument(Guid.NewGuid(), "Legacy", ".pdf", 10, DateTimeOffset.UtcNow);
        var hotel = legacy with { Id = Guid.NewGuid(), Category = LocalDocumentCategory.Accommodation };
        var included = hotel with { Id = Guid.NewGuid(), SourceUrl = "https://example.com/guide.pdf" };
        LocalTripDocument[] documents = [legacy, hotel, included];
        Assert.Equal([hotel], LocalDocumentPolicy.PersonalDocuments(documents, LocalDocumentCategory.Accommodation));
        Assert.Equal([legacy], LocalDocumentPolicy.PersonalDocuments(documents, LocalDocumentCategory.Other));
        Assert.Equal([legacy, hotel], LocalDocumentPolicy.PersonalDocuments(documents));
        Assert.Empty(LocalDocumentPolicy.PersonalDocuments(documents, LocalDocumentCategory.Transport));
    }
}
