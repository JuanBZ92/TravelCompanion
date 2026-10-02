namespace TravelCompanion.Mobile.Services;

public sealed class MauiTripDocumentPicker : ITripDocumentPicker
{
    public async Task<PickedTripDocument?> PickAsync(string title)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = title,
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/pdf", "image/jpeg", "image/png"],
                [DevicePlatform.iOS] = ["com.adobe.pdf", "public.jpeg", "public.png"],
                [DevicePlatform.MacCatalyst] = ["com.adobe.pdf", "public.jpeg", "public.png"],
                [DevicePlatform.WinUI] = [".pdf", ".jpg", ".jpeg", ".png"]
            })
        });
        return file is null ? null : new PickedTripDocument(file.FileName, file.OpenReadAsync);
    }
}
