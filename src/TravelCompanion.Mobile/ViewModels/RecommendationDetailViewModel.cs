using CommunityToolkit.Mvvm.ComponentModel;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class RecommendationDetailViewModel : ViewModelBase, IQueryAttributable
{
    private RecommendationDto? _recommendation;

    public RecommendationDto? Recommendation
    {
        get => _recommendation;
        set => SetProperty(ref _recommendation, value);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Recommendation", out var value) && value is RecommendationDto selectedRecommendation)
        {
            Recommendation = selectedRecommendation;
        }
    }
}
