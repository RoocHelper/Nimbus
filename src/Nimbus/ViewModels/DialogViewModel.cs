using System.Windows.Input;
using System.Windows.Threading;
using Nimbus.Services;

namespace Nimbus.ViewModels;

/// <summary>In-window confirmation dialog (no native message boxes, keeps the look consistent).</summary>
public sealed class DialogViewModel : ObservableObject
{
    private TaskCompletionSource<bool>? _tcs;
    private bool _isOpen;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string _confirmText = string.Empty;
    private string _cancelText = string.Empty;
    private bool _isDanger;
    private IReadOnlyList<string> _items = Array.Empty<string>();

    public DialogViewModel()
    {
        ConfirmCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string ConfirmText
    {
        get => _confirmText;
        private set => SetProperty(ref _confirmText, value);
    }

    public string CancelText
    {
        get => _cancelText;
        private set => SetProperty(ref _cancelText, value);
    }

    public bool IsDanger
    {
        get => _isDanger;
        private set => SetProperty(ref _isDanger, value);
    }

    public IReadOnlyList<string> Items
    {
        get => _items;
        private set
        {
            if (SetProperty(ref _items, value))
            {
                OnPropertyChanged(nameof(HasItems));
            }
        }
    }

    public bool HasItems => Items.Count > 0;

    public Task<bool> ShowAsync(string title, string message, IEnumerable<string>? items = null, string? confirmText = null, bool danger = false)
    {
        _tcs?.TrySetResult(false);
        _tcs = new TaskCompletionSource<bool>();
        Title = title;
        Message = message;
        Items = items?.ToList() ?? new List<string>();
        ConfirmText = confirmText ?? Loc.T("S.Dlg.Continue");
        CancelText = Loc.T("S.Dlg.Cancel");
        IsDanger = danger;
        IsOpen = true;
        return _tcs.Task;
    }

    private void Close(bool result)
    {
        IsOpen = false;
        _tcs?.TrySetResult(result);
        _tcs = null;
    }
}

public sealed class ToastViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private string _message = string.Empty;
    private bool _isVisible;
    private bool _isError;

    public ToastViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            IsVisible = false;
        };
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public void Show(string message, bool isError = false)
    {
        Message = message;
        IsError = isError;
        IsVisible = true;
        _timer.Stop();
        _timer.Start();
    }
}
