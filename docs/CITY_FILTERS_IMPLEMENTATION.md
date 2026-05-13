# City Filters Implementation Guide

## Overview
The Schedule page now features a sophisticated multi-select city filter system that allows users to filter their itinerary by one or more cities.

## Architecture

### Components

#### 1. **CityFilterViewModel**
Located: `/src/TravelCompanion.Mobile/ViewModels/CityFilterViewModel.cs`

Represents a single filter chip with:
- `CityName`: The name of the city
- `IsSelected`: Whether this filter is active
- `IsAllCities`: Special flag for the "All Cities" option
- `BackgroundColor`, `TextColor`, `BorderColor`: Auto-computed based on selection state

```csharp
public class CityFilterViewModel : ObservableObject
{
    public string CityName { get; }
    public bool IsSelected { get; set; }
    public bool IsAllCities { get; }
    
    // Colors update automatically when IsSelected changes
    public Color BackgroundColor { get; }
    public Color TextColor { get; }
    public Color BorderColor { get; }
}
```

#### 2. **ScheduleViewModel**
Updated to support multi-select filtering:

**Properties:**
- `ObservableCollection<CityFilterViewModel> CityFilters` - Dynamic list of filter chips

**Commands:**
- `ToggleCityFilterCommand(string cityName)` - Toggle a specific city filter
- `ClearFiltersCommand()` - Reset all filters to "All Cities"

### How It Works

#### Filter Logic

1. **"All Cities" Behavior:**
   - When clicked: Deselects all other cities
   - When any specific city is selected: "All Cities" is automatically deselected
   - If no cities are selected: "All Cities" becomes selected automatically

2. **Specific City Behavior:**
   - Click to toggle selection on/off
   - Multiple cities can be selected simultaneously
   - Deselecting all cities automatically selects "All Cities"

3. **Filtering:**
   - If "All Cities" is selected OR no specific cities selected → Show all items
   - Otherwise → Show only items from selected cities

#### Code Flow

```
User clicks chip
    ↓
ToggleCityFilterCommand
    ↓
Update IsSelected on CityFilterViewModel(s)
    ↓
ApplyCityFilter()
    ↓
Filter _allItems by selected cities
    ↓
Update Days collection
    ↓
UI updates automatically via INotifyPropertyChanged
```

## XAML Implementation

```xml
<FlexLayout BindableLayout.ItemsSource="{Binding CityFilters}">
    <BindableLayout.ItemTemplate>
        <DataTemplate x:DataType="viewModels:CityFilterViewModel">
            <Border BackgroundColor="{Binding BackgroundColor}"
                    Stroke="{Binding BorderColor}"
                    TextColor="{Binding TextColor}">
                <Border.GestureRecognizers>
                    <TapGestureRecognizer 
                        Command="{Binding Source={x:Reference PageRoot}, Path=BindingContext.ToggleCityFilterCommand}"
                        CommandParameter="{Binding CityName}" />
                </Border.GestureRecognizers>
                <Label Text="{Binding CityName}" />
            </Border>
        </DataTemplate>
    </BindableLayout.ItemTemplate>
</FlexLayout>
```

## Key Features

### 1. **Dynamic City Discovery**
Cities are automatically discovered from schedule items:
```csharp
private void UpdateCityFilters()
{
    var cities = _allItems
        .Select(item => NormalizeCity(item.City))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase);
        
    foreach (var city in cities)
    {
        CityFilters.Add(new CityFilterViewModel(city));
    }
}
```

### 2. **Preserving Selections**
When reloading data, previously selected cities remain selected:
```csharp
var currentSelections = CityFilters
    .Where(f => f.IsSelected)
    .Select(f => f.CityName)
    .ToHashSet();
    
// After rebuilding filters...
foreach (var city in cities)
{
    var isSelected = currentSelections.Contains(city);
    CityFilters.Add(new CityFilterViewModel(city, isSelected));
}
```

### 3. **Visual Feedback**
Colors update automatically when selection changes:
- **Selected**: Accent background (#3D3329), Paper text (#FCFBF9)
- **Unselected**: Mist background (#F5F3F0), Ink text (#1A1714)

## Usage Example

### Backend Data
```csharp
var schedule = new TripScheduleDto
{
    Items = new[]
    {
        new ScheduleItemDto { City = "Tokyo", ... },
        new ScheduleItemDto { City = "Kyoto", ... },
        new ScheduleItemDto { City = "Tokyo", ... },
        new ScheduleItemDto { City = "Osaka", ... },
    }
};
```

### UI Generates
Chips: `[All Cities]` `[Kyoto]` `[Osaka]` `[Tokyo]`

### User Interaction
1. User clicks "Tokyo"
   - Result: Shows only Tokyo items
   - Chips: `[ All Cities ]` `[★ Tokyo]` `[ Kyoto ]` `[ Osaka ]`

2. User clicks "Kyoto" (while Tokyo is selected)
   - Result: Shows Tokyo + Kyoto items
   - Chips: `[ All Cities ]` `[★ Tokyo]` `[★ Kyoto]` `[ Osaka ]`

3. User clicks "Clear filters"
   - Result: Shows all items
   - Chips: `[★ All Cities]` `[ Tokyo]` `[ Kyoto ]` `[ Osaka ]`

## Testing Checklist

- [ ] Selecting "All Cities" deselects other filters
- [ ] Selecting a specific city deselects "All Cities"
- [ ] Multiple cities can be selected simultaneously
- [ ] Deselecting all cities auto-selects "All Cities"
- [ ] "Clear filters" resets to "All Cities"
- [ ] Filter chips update correctly on data reload
- [ ] Previous selections are preserved when reloading
- [ ] Empty city names are normalized to "Unknown City"
- [ ] City names are case-insensitive
- [ ] Visual feedback (colors) updates immediately on tap

## Extending the System

### Adding More Filter Types

You can extend this pattern for other filter dimensions:

```csharp
public class CategoryFilterViewModel : ObservableObject
{
    public string Category { get; }
    public bool IsSelected { get; set; }
    // ... similar to CityFilterViewModel
}

// In ScheduleViewModel
public ObservableCollection<CategoryFilterViewModel> CategoryFilters { get; }

[RelayCommand]
private void ToggleCategoryFilter(string category)
{
    // Similar logic to ToggleCityFilter
}
```

### Dark Theme Support

To add dark theme support, update `CityFilterViewModel`:

```csharp
public Color BackgroundColor => IsSelected 
    ? (Application.Current.RequestedTheme == AppTheme.Dark 
        ? Color.FromArgb("#B8956A")  // PrimaryDark
        : Color.FromArgb("#3D3329"))  // Accent
    : (Application.Current.RequestedTheme == AppTheme.Dark
        ? Color.FromArgb("#2A2520")  // NightSurface
        : Color.FromArgb("#F5F3F0")); // Mist
```

## Performance Considerations

- **Efficient filtering**: Uses HashSet for O(1) lookup
- **Minimal re-renders**: Only updates when necessary via INotifyPropertyChanged
- **Smart selection preservation**: Avoids unnecessary state resets
- **Collection reuse**: Updates ObservableCollection in place when possible

## Troubleshooting

### Filters not updating after data load
**Solution**: Ensure `UpdateCityFilters()` is called in `ApplySchedule()`

### Colors not changing
**Solution**: Verify `OnPropertyChanged` is called for color properties when `IsSelected` changes

### Multiple taps required
**Solution**: Check that `Command` is bound to the page's BindingContext, not the chip's

### Cities duplicated
**Solution**: Ensure `.Distinct()` is used when building city list

## Future Enhancements

- [ ] Search/filter cities by text
- [ ] Remember filter preferences across sessions
- [ ] Date range filters
- [ ] Combine filters (AND/OR logic)
- [ ] Filter presets ("Upcoming", "This Week", etc.)
- [ ] Visual count badges showing items per city
