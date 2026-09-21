namespace TravelCompanion.Mobile.Services;

/// <summary>Keeps an autocomplete field above its results when the keyboard resizes the form.</summary>
internal sealed class SuggestionInputScroller(ScrollView scroll, VisualElement trailingSpace)
{
    private Entry? _entry;
    private int _generation;
    private bool _queued;

    public void Focus(Entry entry)
    {
        Stop();
        _entry = entry;
        entry.Unfocused += OnUnfocused;
        scroll.SizeChanged += OnLayoutChanged;
        if (scroll.Content is { } content) content.SizeChanged += OnLayoutChanged;
        QueueAlignment();
        // Native keyboard animations may finish after the initial focus/layout events.
        var generation = _generation;
        scroll.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
        {
            if (generation == _generation) QueueAlignment();
        });
    }

    public void Stop()
    {
        _generation++;
        if (_entry is not null) _entry.Unfocused -= OnUnfocused;
        _entry = null;
        scroll.SizeChanged -= OnLayoutChanged;
        if (scroll.Content is { } content) content.SizeChanged -= OnLayoutChanged;
        // Retain space/position on blur so tapping a suggestion does not move the form.
    }

    private void OnUnfocused(object? sender, FocusEventArgs e) => Stop();
    private void OnLayoutChanged(object? sender, EventArgs e) => QueueAlignment();

    private void QueueAlignment()
    {
        if (_queued || _entry?.IsFocused != true) return;
        _queued = true;
        scroll.Dispatcher.Dispatch(async () =>
        {
            _queued = false;
            var entry = _entry;
            if (entry?.IsFocused != true || scroll.Handler is null || scroll.Height <= 0) return;
            // Even the final field needs enough content below it to reach the top.
            var space = Math.Ceiling(scroll.Height);
            if (trailingSpace.HeightRequest < space) trailingSpace.HeightRequest = space;
            try
            {
                await scroll.ScrollToAsync(entry, ScrollToPosition.Start, animated: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine($"Suggestion scroll interrupted: {ex.Message}");
            }
        });
    }
}
