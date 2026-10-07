using TravelCompanion.Mobile.ViewModels;
namespace TravelCompanion.Mobile.Tests;

public sealed class ViewModelLoadIdentityTests
{
    private sealed class Model : ViewModelBase
    {
        public Task Start(Func<CancellationToken, Task> action) => LoadAsync(action);
        public void Reset() => ResetLoadState();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Previous_context_cannot_change_new_load_flags_or_errors(bool fail)
    {
        var model = new Model(); var firstRelease = new TaskCompletionSource(); var secondRelease = new TaskCompletionSource();
        var first = model.Start(async _ => { await firstRelease.Task; if (fail) throw new IOException("Old error"); });
        model.Reset(); var second = model.Start(_ => secondRelease.Task);
        firstRelease.SetResult(); await first;
        Assert.True(model.IsBusy); Assert.False(model.HasLoaded); Assert.Null(model.ErrorMessage);
        secondRelease.SetResult(); await second;
        Assert.False(model.IsBusy); Assert.True(model.HasLoaded);
    }
    [Fact]
    public async Task Cancelling_without_new_load_stops_loading_without_reporting_success()
    {
        var model = new Model(); var release = new TaskCompletionSource();
        var load = model.Start(_ => release.Task); model.CancelLoading(); release.SetResult(); await load;
        Assert.False(model.IsBusy); Assert.False(model.HasLoaded); Assert.Null(model.ErrorMessage);
    }
}
