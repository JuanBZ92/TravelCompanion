using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TravelChatPage : ContentPage, IQueryAttributable
{
    private readonly TravelChatViewModel _viewModel;
    private DateOnly? _reviewDate;
    private string? _reviewCity;
    private string? _reviewSummary;
    private DateOnly? _routeDate;
    private string? _routeCity;
    private string? _routeTheme;

    public TravelChatPage()
        : this(MauiProgram.Services.GetRequiredService<TravelChatViewModel>())
    {
    }

    public TravelChatPage(TravelChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _reviewDate = query.TryGetValue("ReviewDate", out var dateValue) && dateValue is DateOnly date
            ? date
            : null;
        _reviewCity = query.TryGetValue("ReviewCity", out var cityValue) ? cityValue as string : null;
        _reviewSummary = query.TryGetValue("ReviewSummary", out var summaryValue) ? summaryValue as string : null;
        _routeDate = query.TryGetValue("RouteDate", out var routeDateValue) && routeDateValue is DateOnly routeDate
            ? routeDate
            : null;
        _routeCity = query.TryGetValue("RouteCity", out var routeCityValue) ? routeCityValue as string : null;
        _routeTheme = query.TryGetValue("RouteTheme", out var routeThemeValue) ? routeThemeValue as string : null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadContextAsync();
        if (_routeDate is { } routeDate && !string.IsNullOrWhiteSpace(_routeTheme))
        {
            var city = _routeCity;
            var theme = _routeTheme;
            _routeDate = null;
            _routeCity = null;
            _routeTheme = null;
            await _viewModel.RequestThematicRouteAsync(routeDate, city, theme);
        }
        else if (_reviewDate is { } reviewDate)
        {
            var city = _reviewCity;
            var summary = _reviewSummary;
            _reviewDate = null;
            _reviewCity = null;
            _reviewSummary = null;
            await _viewModel.RequestDayAlternativeAsync(reviewDate, city, summary);
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelActiveOperations();
        base.OnDisappearing();
    }

    private async void OnSuggestedReplyTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string reply)
        {
            await _viewModel.SendSuggestedReplyCommand.ExecuteAsync(reply);
        }
    }

    private async void OnSaveItineraryItemClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.SaveItineraryItemCommand.ExecuteAsync(card);
        }
    }

    private async void OnOpenRecommendationDetailClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.OpenRecommendationDetailCommand.ExecuteAsync(card);
        }
    }

    private async void OnRequestLessWalkingClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.RequestLessWalkingCommand.ExecuteAsync(card);
        }
    }

    private async void OnReplaceRecommendationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.ReplaceRecommendationCommand.ExecuteAsync(card);
        }
    }

    private async void OnMarkUsefulClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.MarkUsefulCommand.ExecuteAsync(card);
        }
    }

    private async void OnMarkNotUsefulClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.MarkNotUsefulCommand.ExecuteAsync(card);
        }
    }

    private async void OnHideSimilarClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.HideSimilarCommand.ExecuteAsync(card);
        }
    }

    private async void OnTagActionTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatTagActionViewModel tagAction)
        {
            await _viewModel.AvoidTagCommand.ExecuteAsync(tagAction.Tag);
        }
    }

    private async void OnGuidedOptionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatGuidedOptionViewModel option)
        {
            await _viewModel.SelectGuidedOptionCommand.ExecuteAsync(option);
        }
    }

    private void OnAdjustGuidedPlanClicked(object? sender, EventArgs e)
    {
        _viewModel.AdjustGuidedPlanCommand.Execute(null);
    }
}
