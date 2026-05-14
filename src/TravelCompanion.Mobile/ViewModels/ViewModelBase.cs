using CommunityToolkit.Mvvm.ComponentModel;

namespace TravelCompanion.Mobile.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private bool _isRefreshing;
    private bool _hasLoaded;
    private string? _errorMessage;
    private string? _statusMessage;
    private CancellationTokenSource? _loadCancellationTokenSource;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool HasLoaded
    {
        get => _hasLoaded;
        protected set => SetProperty(ref _hasLoaded, value);
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
        catch (OperationCanceledException)
        {
            // Expected when operation is cancelled - don't show error
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
        catch (OperationCanceledException)
        {
            // Expected when operation is cancelled - don't show error
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
        HasLoaded = false;
    }
}
