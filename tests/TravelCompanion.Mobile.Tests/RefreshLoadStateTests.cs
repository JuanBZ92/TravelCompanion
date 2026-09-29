using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Tests;

public sealed class RefreshLoadStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pull_refresh_runs_load_and_stops_indicator_even_on_failure(bool fails)
    {
        var viewModel = new RefreshViewModel { IsRefreshing = true };
        var called = false;
        await viewModel.RunAsync(_ =>
        {
            called = true;
            Assert.True(viewModel.IsBusy);
            if (fails) throw new InvalidOperationException("Unavailable");
            return Task.CompletedTask;
        });
        Assert.True(called);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal(fails, viewModel.HasError);
    }

    [Fact]
    public async Task Native_canceled_message_is_not_shown_to_user()
    {
        var viewModel = new RefreshViewModel();
        await viewModel.RunAsync(_ => throw new HttpRequestException("Canceled"));
        Assert.True(viewModel.HasError);
        Assert.DoesNotContain("Canceled", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Caller_cancellation_is_silent_but_timeout_is_not()
    {
        var viewModel = new RefreshViewModel();
        await viewModel.RunAsync(ct => { viewModel.CancelLoading(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        Assert.False(viewModel.HasError);
        await viewModel.RunAsync(_ => throw new TaskCanceledException());
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    private sealed class RefreshViewModel : ViewModelBase
    {
        public Task RunAsync(Func<CancellationToken, Task> load) => LoadAsync(load);
    }
}
