namespace TravelCompanion.Mobile.Services;

/// <summary>Reveals room for suggestions without aligning the input to the top.</summary>
internal sealed class SuggestionInputScroller(ScrollView scroll)
{
    private Entry? _entry;
    private int _generation;

    public void Focus(object? sender, FocusEventArgs args)
    {
        Stop();
        if (sender is not Entry entry) return;
        _entry = entry;
        entry.Unfocused += OnUnfocused;
        scroll.SizeChanged += OnLayoutChanged;
        if (scroll.Content is { } content) content.SizeChanged += OnLayoutChanged;
        RequestAdjustment();
    }

    public void Stop()
    {
        ++_generation;
        if (_entry is { } entry) entry.Unfocused -= OnUnfocused;
        scroll.SizeChanged -= OnLayoutChanged;
        if (scroll.Content is { } content) content.SizeChanged -= OnLayoutChanged;
        _entry = null;
    }

    private void OnUnfocused(object? sender, FocusEventArgs args) => Stop();
    private void OnLayoutChanged(object? sender, EventArgs args) => RequestAdjustment();

    private void RequestAdjustment()
    {
        var generation = ++_generation;
        // Wait for the keyboard and suggestion rows to finish changing the viewport.
        scroll.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), async () =>
        {
            if (generation != _generation || _entry is not { IsFocused: true } entry
                || scroll.Handler is null || scroll.Height <= 0) return;
            var top = entry.Y;
            Element? parent = entry.Parent;
            while (parent is VisualElement visual && parent != scroll.Content)
            {
                top += visual.Y;
                parent = visual.Parent;
            }
            if (parent != scroll.Content) return;
            var target = SuggestionScrollOffset.Calculate(scroll.ScrollY, scroll.Height, top, entry.Height);
            if (target > scroll.ScrollY + 1)
            {
                try { await scroll.ScrollToAsync(0, target, true); }
                catch (ObjectDisposedException) { /* The page closed during the animation. */ }
            }
        });
    }
}
