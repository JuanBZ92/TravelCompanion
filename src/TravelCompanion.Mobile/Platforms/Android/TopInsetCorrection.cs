using AndroidX.Core.View;

namespace TravelCompanion.Mobile.Platforms.Android;

// Shell can deliver its first layout before the page's safe area is applied.
// Add only the uncovered inset; when Shell corrects its position, remove it.
internal sealed class TopInsetCorrection
{
    private readonly Grid layout;
    private readonly VisualElement anchor;
    private readonly Thickness originalPadding;
    private global::Android.Views.View? native;
    private global::Android.Views.ViewTreeObserver? observer;
    private readonly int[] position = new int[2];

    private TopInsetCorrection(Grid layout, VisualElement anchor)
    {
        this.layout = layout;
        this.anchor = anchor;
        originalPadding = layout.Padding;
        layout.Loaded += OnLoaded;
        layout.Unloaded += OnUnloaded;
        layout.HandlerChanged += OnHandlerChanged;
    }

    public static void Observe(Grid layout, VisualElement anchor) => _ = new TopInsetCorrection(layout, anchor);

    private void OnLoaded(object? sender, EventArgs e) => Attach();
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        Detach();
        if (layout.IsLoaded) Attach();
    }
    private void OnUnloaded(object? sender, EventArgs e) => Detach();

    private void Attach()
    {
        Detach();
        native = layout.Handler?.PlatformView as global::Android.Views.View;
        observer = native?.ViewTreeObserver;
        if (observer is not { IsAlive: true }) return;
        observer.GlobalLayout += OnLayout;
        // Request fresh insets without replacing MAUI's own inset listener.
        ViewCompat.RequestApplyInsets(native!);
        Update();
    }

    private void OnLayout(object? sender, EventArgs e) => Update();
    private void Update()
    {
        if (native is null || !native.IsAttachedToWindow || native.Height == 0) return;
        var insets = ViewCompat.GetRootWindowInsets(native);
        if (insets is null) return;
        var top = insets.GetInsets(WindowInsetsCompat.Type.StatusBars() | WindowInsetsCompat.Type.DisplayCutout())?.Top ?? 0;
        if (anchor.Handler?.PlatformView is not global::Android.Views.View header || header.Height == 0) return;
        header.GetLocationInWindow(position);
        var density = native.Resources?.DisplayMetrics?.Density ?? 1f;
        // Measure the actual header, including MAUI's own internal safe-area padding.
        // Remove our previous contribution to avoid alternating padding each layout.
        var existingCorrection = (layout.Padding.Top - originalPadding.Top) * density;
        var uncovered = Math.Max(0, top - (position[1] - existingCorrection)) / density;
        var padding = new Thickness(originalPadding.Left, originalPadding.Top + uncovered, originalPadding.Right, originalPadding.Bottom);
        if (Math.Abs(layout.Padding.Top - padding.Top) > 0.1) layout.Padding = padding;
    }

    private void Detach()
    {
        if (observer is { IsAlive: true }) observer.GlobalLayout -= OnLayout;
        observer = null;
        native = null;
    }
}
