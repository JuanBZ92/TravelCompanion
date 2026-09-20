# Mobile
- Follow existing Pages/ViewModels/Services organization and CommunityToolkit.Mvvm conventions.
- Preserve localization bindings: root `Directory.Build.props` intentionally disables XamlC Source-binding compilation for this project.
- Keep platform-specific code under Platforms and validate the target affected by a change.
- `tests/TravelCompanion.Mobile.Tests` links selected service/ViewModel sources; passing it does not validate XAML, native integrations or the full MAUI application.
