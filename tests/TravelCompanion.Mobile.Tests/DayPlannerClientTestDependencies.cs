namespace TravelCompanion.Mobile.Services;

// Production diagnostics are exercised separately. Client tests supply a controlled HTTP handler.
public sealed class DiagnosticHttpHandler(HttpMessageHandler inner) : DelegatingHandler(inner);
