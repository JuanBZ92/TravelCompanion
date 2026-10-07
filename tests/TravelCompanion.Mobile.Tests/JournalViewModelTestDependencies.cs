using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

// Native page and image boundaries only. Tests execute the production Journal ViewModel.
public sealed class ImageSource
{
    public string? File { get; private init; }
    public Func<Stream>? OpenStream { get; private init; }
    public static ImageSource FromFile(string file) => new() { File = file };
    public static ImageSource FromStream(Func<Stream> stream) => new() { OpenStream = stream };
}
public enum DevicePlatform { Android, iOS, WinUI }
public static class DeviceInfo { public static DevicePlatform Platform { get; set; } = DevicePlatform.Android; }

namespace TravelCompanion.Mobile.Pages
{
    public sealed record JournalMemoryPage(JournalScope scope, JournalMemory memory, ScheduleItemDto? item,
        bool startWithPhotos = false, JournalDraft? draft = null);
    public sealed record JournalReadingPage(JournalScope scope, JournalMemory memory, ScheduleItemDto? item);
    public sealed record JournalPhotoPage(JournalScope scope, JournalMemory memory, int index);
    public sealed record JournalActivityPickerPage(IReadOnlyList<ScheduleItemDto> items, IReadOnlyList<JournalMemory> memories);
    public sealed record JournalExportPage(JournalScope scope, string tripTitle, IReadOnlyList<JournalMemory> memories);
}
