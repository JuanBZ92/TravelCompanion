namespace TravelCompanion.Mobile.Services;

public static class JournalText
{
    public static string Get(string key) => LocalizationResourceManager.Instance[key];
    public static string Format(string key, params object[] values) => string.Format(Get(key), values);
    public static string Title(JournalMemory memory) => string.IsNullOrWhiteSpace(memory.Title)
        ? Format("JournalDateTitle", memory.Date.ToString("M")) : memory.Title;
    public static string DisplayTitle(JournalMemory memory) => !string.IsNullOrWhiteSpace(memory.Title)
        ? memory.Title : !string.IsNullOrWhiteSpace(memory.City) ? memory.City : Title(memory);
    public static string Memories(int count) => Format(count == 1 ? "JournalOneMemory" : "JournalManyMemories", count);
    public static string Photos(int count) => Format(count == 1 ? "JournalOnePhoto" : "JournalManyPhotos", count);

    public static bool HasPendingChanges(JournalMemory memory) => memory.Pending is not null
        || memory.FreePending is not null || memory.DeletePending is not null;

    public static string SynchronizationStatus(JournalMemory memory, JournalSynchronizationState state)
    {
        if (memory.HasConflict) return Get("JournalConflict");
        if (memory.IsDraft) return "";
        if (!HasPendingChanges(memory)) return memory.Revision > 0 ? Get("JournalSynced") : "";
        return Get(state switch
        {
            JournalSynchronizationState.Synchronizing => "JournalSyncing",
            JournalSynchronizationState.Offline => "JournalSyncOffline",
            JournalSynchronizationState.Failed => "JournalSyncFailed",
            _ => "JournalPending"
        });
    }

    public static bool ShouldRetrySynchronization(JournalMemory memory, JournalSynchronizationState state) =>
        !memory.IsDraft && !memory.HasConflict && HasPendingChanges(memory)
        && state is not (JournalSynchronizationState.Synchronizing or JournalSynchronizationState.Offline);
}
