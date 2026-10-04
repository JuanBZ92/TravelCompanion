using Microsoft.Maui.Controls.Shapes;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

/// <summary>Shared editorial controls for travel pages. Interactive controls keep a 48 dp touch target.</summary>
internal static class EditorialUi
{
    public static Color Paper => Resource("Mist", "#F8F3ED");
    public static Color Surface => Resource("EditorialSurface", "#FFFCF8");
    public static Color Ink => Resource("Ink", "#1A1714");
    public static Color Muted => Resource("EditorialMuted", "#71675D");
    public static Color Accent => Resource("Accent", "#3D3329");
    public static Color Line => Resource("EditorialLine", "#E5DDD3");
    public static Color Gold => Resource("Gold", "#B8956A");
    public static string TextResource(string key) => LocalizationResourceManager.Instance[key];

    public static Label Text(string text, double size = 15) => new()
    {
        Text = text, FontSize = size, TextColor = Muted, LineHeight = 1.2,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Heading(string text, double size = 30,
        SemanticHeadingLevel level = SemanticHeadingLevel.Level1)
    {
        var label = Text(text, size);
        label.FontFamily = "serif";
        label.TextColor = Ink;
        SemanticProperties.SetHeadingLevel(label, level);
        return label;
    }

    public static Border Card(View content, double padding = 16) => new()
    {
        Content = content, Padding = padding, BackgroundColor = Surface, Stroke = Line,
        StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) }
    };

    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, MinimumHeightRequest = 48, MinimumWidthRequest = 48,
            FontSize = 15, CornerRadius = 14, Padding = new Thickness(16, 12),
            BackgroundColor = primary ? Accent : Colors.Transparent,
            TextColor = primary ? Colors.White : Ink
        };
        button.Clicked += async (_, _) => await RunAsync(button, action);
        return button;
    }

    public static ImageButton Icon(string source, string description, Func<Task> action)
    {
        var button = new ImageButton
        {
            Source = source, WidthRequest = 48, HeightRequest = 48,
            MinimumHeightRequest = 48, MinimumWidthRequest = 48,
            Padding = 12, BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(button, description);
        ToolTipProperties.SetText(button, description);
        button.Clicked += async (_, _) => await RunAsync(button, action);
        return button;
    }

    public static void RevealError(ScrollView scroll, Label message)
    {
        scroll.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), async () =>
        {
            if (scroll.Handler is null || message.Handler is null || string.IsNullOrWhiteSpace(message.Text)) return;
            try
            {
                await scroll.ScrollToAsync(message, ScrollToPosition.MakeVisible, true);
                if (message.Handler is not null) message.SetSemanticFocus();
            }
            catch (ObjectDisposedException) { }
        });
    }

    private static async Task RunAsync(VisualElement control, Func<Task> action)
    {
        if (!control.IsEnabled) return;
        control.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ClientDiagnostics.Record("editorial_action_failed", exception: exception);
            await Shell.Current.DisplayAlertAsync(TextResource("UxActionFailedTitle"),
                TextResource("UxActionFailed"), TextResource("UxOk"));
        }
        finally { control.IsEnabled = true; }
    }

    private static Color Resource(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color : Color.FromArgb(fallback);
}
