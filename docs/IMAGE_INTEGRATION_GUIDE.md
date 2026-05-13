# Image Integration Guide for Travel Companion

## Overview
This guide explains how to integrate real images into your MAUI application to replace the current placeholders.

## Current Placeholders
Currently, the app uses placeholder emojis (📸, 🍣, etc.) in several places:
- **RecommendationsPage**: Card images
- **RecommendationDetailPage**: Hero image
- **MapPage**: Map fallback

## Recommended Approach: Using Unsplash API

### 1. Add Unsplash Package
```bash
dotnet add package UnsplashSharp
```

### 2. Store API Key Securely
Add to `appsettings.json` or Azure Key Vault:
```json
{
  "Unsplash": {
    "AccessKey": "YOUR_UNSPLASH_ACCESS_KEY",
    "SecretKey": "YOUR_UNSPLASH_SECRET_KEY"
  }
}
```

### 3. Create Image Service
Create `/src/TravelCompanion.Mobile/Services/ImageService.cs`:

```csharp
public interface IImageService
{
    Task<string> GetRecommendationImageUrl(string category, string title);
    Task<byte[]> DownloadImageAsync(string url);
}

public class UnsplashImageService : IImageService
{
    private readonly UnsplasharpClient _client;
    private readonly HttpClient _httpClient;

    public UnsplashImageService(IConfiguration config, HttpClient httpClient)
    {
        var accessKey = config["Unsplash:AccessKey"];
        _client = new UnsplasharpClient(accessKey);
        _httpClient = httpClient;
    }

    public async Task<string> GetRecommendationImageUrl(string category, string title)
    {
        // Map Japanese categories to English search terms
        var searchTerms = new Dictionary<string, string>
        {
            { "寿司", "sushi restaurant japan" },
            { "寺院", "japanese temple shrine" },
            { "市場", "japanese market food" },
            { "懐石", "kaiseki japanese cuisine" }
        };

        var query = searchTerms.ContainsKey(category) 
            ? searchTerms[category] 
            : $"{title} japan";

        var photos = await _client.SearchPhoto(query, 1, 1);
        return photos?.FirstOrDefault()?.Urls.Regular;
    }

    public async Task<byte[]> DownloadImageAsync(string url)
    {
        return await _httpClient.GetByteArrayAsync(url);
    }
}
```

### 4. Update Recommendation DTOs
Add `ImageUrl` property to recommendation DTOs:

```csharp
public class RecommendationDto
{
    // ... existing properties
    public string ImageUrl { get; set; }
    public string ThumbnailUrl { get; set; }
}
```

### 5. Update XAML to Use Real Images

**RecommendationsPage.xaml** - Replace placeholder:
```xml
<Border.StrokeShape>
    <RoundRectangle CornerRadius="12,12,0,0" />
</Border.StrokeShape>
<Image Source="{Binding ImageUrl}"
       Aspect="AspectFill"
       HeightRequest="200">
    <Image.GestureRecognizers>
        <TapGestureRecognizer Command="{Binding Source={x:Reference PageRoot}, Path=BindingContext.OpenRecommendationCommand}"
                              CommandParameter="{Binding .}" />
    </Image.GestureRecognizers>
</Image>
```

**RecommendationDetailPage.xaml** - Replace hero image:
```xml
<Border HeightRequest="300"
        BackgroundColor="{AppThemeBinding Light={StaticResource Mist}, Dark={StaticResource NightSurfaceAlt}}">
    <Border.StrokeShape>
        <RoundRectangle CornerRadius="0" />
    </Border.StrokeShape>
    <Image Source="{Binding Recommendation.ImageUrl}"
           Aspect="AspectFill" />
</Border>
```

### 6. Implement Caching
Use `FileCache` to avoid repeated downloads:

```csharp
public class CachedImageService : IImageService
{
    private readonly IImageService _innerService;
    private readonly string _cacheDir;

    public CachedImageService(IImageService innerService)
    {
        _innerService = innerService;
        _cacheDir = Path.Combine(FileSystem.CacheDirectory, "images");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<string> GetCachedImagePath(string imageUrl)
    {
        var fileName = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(imageUrl)).Replace("/", "_");
        var filePath = Path.Combine(_cacheDir, fileName);

        if (File.Exists(filePath))
            return filePath;

        var imageData = await _innerService.DownloadImageAsync(imageUrl);
        await File.WriteAllBytesAsync(filePath, imageData);
        return filePath;
    }
}
```

## Alternative: Backend Image Management

### Option 1: Azure Blob Storage
Store curated images in Azure Blob Storage:
- Create container `recommendation-images`
- Upload high-quality photos
- Return blob URLs from API

### Option 2: Cloudinary
- Automatic optimization
- Responsive images
- CDN distribution

```csharp
public string GetCloudinaryUrl(string publicId, int width = 800, int height = 600)
{
    return $"https://res.cloudinary.com/{cloudName}/image/upload/c_fill,w_{width},h_{height}/{publicId}";
}
```

## Implementation Checklist

- [ ] Choose image source (Unsplash, Azure Blob, Cloudinary)
- [ ] Add API keys to secure configuration
- [ ] Create `IImageService` interface and implementation
- [ ] Update DTOs with `ImageUrl` properties
- [ ] Update backend API to populate image URLs
- [ ] Update XAML to use `Image` instead of placeholders
- [ ] Implement image caching
- [ ] Test on different screen sizes
- [ ] Optimize image sizes for mobile
- [ ] Handle offline/error states gracefully

## Best Practices

1. **Lazy Loading**: Only load images when scrolling into view
2. **Placeholder Strategy**: Show low-res placeholder while loading
3. **Error Handling**: Fallback to default image on failure
4. **Optimization**: 
   - Use WebP format where supported
   - Resize images server-side
   - Implement progressive loading
5. **Accessibility**: Always provide `SemanticProperties.Description`

## Example: Complete Image Implementation

```xml
<Image>
    <Image.Source>
        <UriImageSource Uri="{Binding ImageUrl}"
                       CachingEnabled="True"
                       CacheValidity="7" />
    </Image.Source>
    <Image.Behaviors>
        <toolkit:ImageLoadingBehavior>
            <toolkit:ImageLoadingBehavior.LoadingTemplate>
                <DataTemplate>
                    <ActivityIndicator IsRunning="True" />
                </DataTemplate>
            </toolkit:ImageLoadingBehavior.LoadingTemplate>
            <toolkit:ImageLoadingBehavior.FailedTemplate>
                <DataTemplate>
                    <Label Text="Failed to load image" />
                </DataTemplate>
            </toolkit:ImageLoadingBehavior.FailedTemplate>
        </toolkit:ImageLoadingBehavior>
    </Image.Behaviors>
</Image>
```

## Notes
- Consider using `FFImageLoading.Maui` for advanced caching and transformations
- Test image loading on slow connections
- Implement retry logic for failed downloads
