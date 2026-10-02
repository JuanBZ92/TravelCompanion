using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Shared.Tests;

public sealed class EditorialReviewPolicyTests
{
    [Fact]
    public void Missing_future_and_old_reviews_require_confirmation()
    {
        var today = new DateOnly(2026, 10, 2);
        Assert.True(EditorialReviewPolicy.NeedsReview(null, today));
        Assert.True(EditorialReviewPolicy.NeedsReview(today.AddDays(1), today));
        Assert.True(EditorialReviewPolicy.NeedsReview(today.AddDays(-91), today));
        Assert.False(EditorialReviewPolicy.NeedsReview(today.AddDays(-90), today));
        Assert.False(EditorialReviewPolicy.NeedsReview(today, today));
    }
}
