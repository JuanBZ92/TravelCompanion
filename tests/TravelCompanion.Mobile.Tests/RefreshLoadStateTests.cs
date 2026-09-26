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

    private sealed class RefreshViewModel : ViewModelBase
    {
        public Task RunAsync(Func<CancellationToken, Task> load) => LoadAsync(load);
    }
}
