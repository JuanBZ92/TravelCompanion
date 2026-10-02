namespace TravelCompanion.Mobile.Services;

public static class JournalText
{
    public static string Get(string key) => LocalizationResourceManager.Instance[key];
    public static string Format(string key, params object[] values) => string.Format(Get(key), values);
    public static string Title(JournalMemory memory) => string.IsNullOrWhiteSpace(memory.Title)
        ? Format("JournalDateTitle", memory.Date.ToString("M")) : memory.Title;
    public static string Memories(int count) => Format(count == 1 ? "JournalOneMemory" : "JournalManyMemories", count);
    public static string Photos(int count) => Format(count == 1 ? "JournalOnePhoto" : "JournalManyPhotos", count);
}
