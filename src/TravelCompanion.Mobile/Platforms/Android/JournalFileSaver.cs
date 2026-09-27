using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;

namespace TravelCompanion.Mobile.Platforms.Android;

public static class JournalFileSaver
{
    private static ActivityResultLauncher? launcher;
    private static TaskCompletionSource<global::Android.Net.Uri?>? pending;
    public static void Register(MainActivity activity)
    {
        pending?.TrySetCanceled();
        pending = null;
        launcher = activity.RegisterForActivityResult(new ActivityResultContracts.CreateDocument("application/pdf"), new Callback());
    }
    public static async Task<bool> SaveAsync(string path)
    {
        if (launcher is null || pending is not null) throw new InvalidOperationException("El selector de archivos no está disponible.");
        var completion = new TaskCompletionSource<global::Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        try
        {
            launcher.Launch(new Java.Lang.String("Mi Journal.pdf"));
            var uri = await completion.Task;
            if (uri is null) return false;
            using var output = global::Android.App.Application.Context.ContentResolver!.OpenOutputStream(uri)
                ?? throw new IOException("No se pudo abrir el destino.");
            await using var input = File.OpenRead(path);
            await input.CopyToAsync(output);
            return true;
        }
        finally { pending = null; }
    }
    private sealed class Callback : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result) => pending?.TrySetResult(result as global::Android.Net.Uri);
    }
}
