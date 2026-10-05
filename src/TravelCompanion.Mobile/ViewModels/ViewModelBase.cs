using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private static string ConnectionError => Services.LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"];

    protected static bool IsConnectionError(Exception exception)
    {
        if (exception is HttpRequestException or IOException or OperationCanceledException) return true;
        // AndroidMessageHandler wraps native IO failures in WebException.
        // An HTTP protocol response must keep its existing error handling.
        if (exception is System.Net.WebException
            { Status: not System.Net.WebExceptionStatus.ProtocolError, InnerException: { } inner })
            return IsConnectionError(inner);

        // Android native IO exceptions do not inherit System.IO.IOException.
        // Check the hierarchy without referencing Android types in other targets.
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == "Java.IO.IOException") return true;
        }
        return false;
    }
    private bool _isBusy;
    private bool _isRefreshing;
    private bool _hasLoaded;
    private string? _errorMessage;
    private string? _statusMessage;
    private string? _lastUpdatedMessage;
    private CancellationTokenSource? _loadCancellationTokenSource;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(IsInitialLoading));
                OnLoadStateChanged();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool IsInitialLoading => IsBusy && !HasLoaded;

    public bool HasLoaded
    {
        get => _hasLoaded;
        protected set
        {
            if (SetProperty(ref _hasLoaded, value))
            {
                OnPropertyChanged(nameof(IsInitialLoading));
                OnLoadStateChanged();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public string? LastUpdatedMessage
    {
        get => _lastUpdatedMessage;
        set
        {
            if (SetProperty(ref _lastUpdatedMessage, value))
            {
                OnPropertyChanged(nameof(HasLastUpdated));
            }
        }
    }

    public bool HasLastUpdated => !string.IsNullOrWhiteSpace(LastUpdatedMessage);

    protected async Task LoadAsync(Func<Task> loadAction)
    {
        if (IsBusy)
        {
            return;
        }

        // Cancel any previous load operation
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            await loadAction();
            HasLoaded = true;
        }
        catch (OperationCanceledException) when (_loadCancellationTokenSource?.IsCancellationRequested == true)
        {
            // Expected when operation is cancelled - don't show error
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            if (_loadCancellationTokenSource?.IsCancellationRequested != true) ErrorMessage = ConnectionError;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    protected async Task LoadAsync(Func<CancellationToken, Task> loadAction)
    {
        if (IsBusy)
        {
            return;
        }

        // Cancel any previous load operation
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = new CancellationTokenSource();

        var cancellationToken = _loadCancellationTokenSource.Token;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            await loadAction(cancellationToken);
            HasLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when operation is cancelled - don't show error
        }
        catch (Exception ex) when (IsConnectionError(ex))
        {
            if (!cancellationToken.IsCancellationRequested) ErrorMessage = ConnectionError;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    protected void ResetLoadState()
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = null;
        IsBusy = false;
        IsRefreshing = false;
        ErrorMessage = null;
        StatusMessage = null;
        LastUpdatedMessage = null;
        HasLoaded = false;
    }

    public void CancelLoading()
    {
        _loadCancellationTokenSource?.Cancel();
    }

    protected void MarkLastUpdated(DateTimeOffset savedAt)
    {
        LastUpdatedMessage = $"Actualizado {savedAt.ToLocalTime():dd/MM HH:mm}";
    }

    protected virtual void OnLoadStateChanged()
    {
    }
}
