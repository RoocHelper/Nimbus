using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nimbus.Services;

namespace Nimbus.ViewModels;

public enum JobState
{
    Idle,
    Running,
    Succeeded,
    Warning,
    Failed,
}

public sealed record JobOutcome(bool Success, bool HasWarnings, string? LogFile);

/// <summary>Runs one long operation at a time and drives the progress overlay.</summary>
public sealed class JobViewModel : ObservableObject
{
    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();
    private readonly DispatcherTimer _flushTimer;
    private bool _logDirty;

    private JobState _state = JobState.Idle;
    private string _title = string.Empty;
    private string _status = string.Empty;
    private double _percent;
    private bool _isIndeterminate = true;
    private bool _showLog;
    private bool _isSilent;
    private bool _restartHint;
    private string _logText = string.Empty;
    private string? _logFile;

    public JobViewModel()
    {
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _flushTimer.Tick += (_, _) => FlushLog();

        CloseCommand = new RelayCommand(Close, () => State != JobState.Running);
        ToggleLogCommand = new RelayCommand(() => ShowLog = !ShowLog);
        OpenLogFileCommand = new RelayCommand(OpenLogFile, () => _logFile is not null && State != JobState.Running);
    }

    public ICommand CloseCommand { get; }
    public ICommand ToggleLogCommand { get; }
    public ICommand OpenLogFileCommand { get; }

    public JobState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsOverlayVisible));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(ResultText));
                CommandManager.InvalidateRequerySuggested();
                BusyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? BusyChanged;

    public bool IsBusy => State == JobState.Running;
    public bool IsFinished => State is JobState.Succeeded or JobState.Warning or JobState.Failed;
    public bool IsOverlayVisible => State != JobState.Idle && !_isSilent;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public bool ShowLog
    {
        get => _showLog;
        set => SetProperty(ref _showLog, value);
    }

    public bool RestartHint
    {
        get => _restartHint;
        private set => SetProperty(ref _restartHint, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string ResultText => State switch
    {
        JobState.Succeeded => Loc.T("S.Job.Done"),
        JobState.Warning => Loc.T("S.Job.DoneWarnings"),
        JobState.Failed => Loc.T("S.Job.Failed"),
        _ => Loc.T("S.Job.Running"),
    };

    public Task<JobOutcome> RunScriptAsync(string title, string scriptBody, bool silent = false, bool restartHint = false)
    {
        return RunAsync(title, async log =>
        {
            var result = await PowerShellRunner.RunAsync(scriptBody, log);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(Loc.Format("S.Job.ExitCode", result.ExitCode));
            }

            return result.WarningCount + result.ErrorCount > 0;
        }, silent, restartHint);
    }

    /// <param name="work">Receives a line logger; returns true when the work finished with warnings.</param>
    public async Task<JobOutcome> RunAsync(string title, Func<Action<string>, Task<bool>> work, bool silent = false, bool restartHint = false)
    {
        if (IsBusy)
        {
            return new JobOutcome(false, false, null);
        }

        lock (_logGate)
        {
            _log.Clear();
        }

        LogText = string.Empty;
        _logFile = null;
        _isSilent = silent;
        Title = title;
        Status = Loc.T("S.Job.Preparing");
        Percent = 0;
        IsIndeterminate = true;
        ShowLog = false;
        RestartHint = false;
        State = JobState.Running;
        _flushTimer.Start();

        AppendLog($"== {title} — {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

        var hasWarnings = false;
        var success = true;
        try
        {
            hasWarnings = await Task.Run(() => work(HandleLine));
        }
        catch (Exception ex)
        {
            success = false;
            AppendLog("ERROR: " + ex.Message);
        }

        _flushTimer.Stop();
        FlushLog();
        _logFile = SaveLog(title);

        RestartHint = restartHint && success;
        Percent = 100;
        IsIndeterminate = false;
        Status = success ? Loc.T("S.Job.DoneStatus") : Loc.T("S.Job.FailedStatus");
        State = !success ? JobState.Failed : hasWarnings ? JobState.Warning : JobState.Succeeded;

        if (silent)
        {
            State = JobState.Idle;
        }

        return new JobOutcome(success, hasWarnings, _logFile);
    }

    public void Close()
    {
        if (State != JobState.Running)
        {
            State = JobState.Idle;
        }
    }

    /// <summary>Lets non-PowerShell work report progress.</summary>
    public void Report(int percent, string status)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (percent >= 0)
            {
                IsIndeterminate = false;
                Percent = Math.Clamp(percent, 0, 100);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                Status = status;
            }
        });
    }

    private void HandleLine(string line)
    {
        if (line.StartsWith(PowerShellRunner.ProtocolPrefix, StringComparison.Ordinal))
        {
            var payload = line[PowerShellRunner.ProtocolPrefix.Length..];
            var parts = payload.Split('|');
            switch (parts[0])
            {
                case "PROGRESS" when parts.Length >= 3:
                    _ = int.TryParse(parts[1], out var p);
                    Report(p, string.Join("|", parts.Skip(2)));
                    return;
                case "LOG" when parts.Length >= 4:
                    var message = string.Join("|", parts.Skip(3));
                    AppendLog(parts[1] == "INFO" ? $"  · {message}" : $"{parts[1]}: {message}");
                    return;
                case "ERROR" when parts.Length >= 2:
                    AppendLog("ERROR: " + string.Join("|", parts.Skip(1)));
                    return;
                case "APPX":
                    return;
            }
        }

        AppendLog(line);
    }

    private void AppendLog(string line)
    {
        lock (_logGate)
        {
            _log.AppendLine(line);
            _logDirty = true;
        }
    }

    private void FlushLog()
    {
        string? text = null;
        lock (_logGate)
        {
            if (_logDirty)
            {
                text = _log.ToString();
                _logDirty = false;
            }
        }

        if (text is not null)
        {
            LogText = text;
        }
    }

    private string? SaveLog(string title)
    {
        try
        {
            var safe = new string(title.Where(char.IsLetterOrDigit).Take(32).ToArray());
            var path = Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}.log");
            lock (_logGate)
            {
                File.WriteAllText(path, _log.ToString(), Encoding.UTF8);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    private void OpenLogFile()
    {
        if (_logFile is null || !File.Exists(_logFile))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_logFile}\"") { UseShellExecute = true });
    }
}
