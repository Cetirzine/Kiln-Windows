using System.Globalization;

namespace Kiln.Core;

/// 一个后台任务（安装版本、下载 Java、下载 Mod 等），在“下载管理”中显示。
/// 对应原版的 @MainActor @Observable Job；这里改为线程安全，UI 侧负责调度到主线程。
public sealed class Job
{
    public enum JobState { Running, Success, Failed, Cancelled }

    private readonly object _gate = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public bool Silent { get; }
    public event Action<Job, JobState>? StateChanged;
    private string _stage = "";
    private int _stageIndex;
    private int _stageCount = 1;
    private double _fileFraction;
    private string _fileText = "";
    private double _speed;
    private JobState _state = JobState.Running;
    private string? _error;
    private CancellationTokenSource? _cts = new();

    public Job(string title, bool silent = false) { Title = title; Silent = silent; }

    public string Stage { get { lock (_gate) return _stage; } set { lock (_gate) _stage = value; } }
    public int StageIndex { get { lock (_gate) return _stageIndex; } private set { lock (_gate) _stageIndex = value; } }
    public int StageCount { get { lock (_gate) return _stageCount; } private set { lock (_gate) _stageCount = value; } }
    public double FileFraction { get { lock (_gate) return _fileFraction; } set { lock (_gate) _fileFraction = value; } }
    public string FileText { get { lock (_gate) return _fileText; } set { lock (_gate) _fileText = value; } }
    public double Speed { get { lock (_gate) return _speed; } set { lock (_gate) _speed = value; } }
    public JobState State
    {
        get { lock (_gate) return _state; }
        set
        {
            lock (_gate) { if (_state == value) return; _state = value; }
            StateChanged?.Invoke(this, value);
        }
    }
    public string? Error { get { lock (_gate) return _error; } set { lock (_gate) _error = value; } }

    /// 取消令牌；任务通过它协作取消（对应原版 Job.task?.cancel()）
    public CancellationToken Token
    {
        get { lock (_gate) return _cts?.Token ?? default; }
    }

    public double Fraction
    {
        get
        {
            lock (_gate)
            {
                if (_stageCount <= 0) return 0;
                if (_state == JobState.Success) return 1;
                return Math.Min(1, (_stageIndex + _fileFraction) / _stageCount);
            }
        }
    }

    /// 阶段进度：当前阶段序号 / 总阶段数
    public void Enter(string stage, int index, int of)
    {
        lock (_gate)
        {
            _stage = stage;
            _stageIndex = index;
            _stageCount = of;
            _fileFraction = 0;
            _fileText = "";
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation; lock (_gate) cancellation = _cts;
        cancellation?.Cancel();
    }

    public void Fail(string error) { Error = error; State = JobState.Failed; }

    public static string FormatSpeed(double bytesPerSecond) => FormatSize(bytesPerSecond) + "/s";

    public static string FormatSize(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = Math.Max(0, bytes);
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{(long)v} B" : v.ToString("F1", CultureInfo.InvariantCulture) + " " + units[i];
    }
}
