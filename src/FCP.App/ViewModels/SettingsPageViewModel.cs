using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCP.Core.Java;
using FCP.Core.Platform;
using FCP.Core.Settings;

namespace FCP.App.ViewModels;

public enum SettingsSection
{
    Launch,
    Personalization,
    Download,
    Other,
}

/// <summary>设置页：修改即保存到 settings.json。</summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly SettingsStore _store;
    private readonly JavaManager _javaManager;
    private readonly ISystemInfo _system;
    private readonly IShell _platformShell;
    private GlobalSettings? _settings;
    private bool _loading;

    public SettingsPageViewModel(ShellViewModel shell, LauncherState state, SettingsStore store, JavaManager javaManager, ISystemInfo system, IShell platformShell)
    {
        _platformShell = platformShell;
        _shell = shell;
        State = state;
        _store = store;
        _javaManager = javaManager;
        _system = system;
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LauncherState.Javas))
                OnPropertyChanged(nameof(Javas));
        };
        _ = LoadAsync();
    }

    public LauncherState State { get; }

    public IReadOnlyList<JavaRuntime> Javas => State.Javas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaunchSection), nameof(IsDownloadSection), nameof(IsOtherSection), nameof(IsComingSoonSection))]
    private SettingsSection _section = SettingsSection.Launch;

    public bool IsLaunchSection => Section == SettingsSection.Launch;
    public bool IsDownloadSection => Section == SettingsSection.Download;
    public bool IsOtherSection => Section == SettingsSection.Other;
    public bool IsComingSoonSection => Section == SettingsSection.Personalization;

    public string ConfigDirectory => State.Paths.ConfigDirectory;
    public string DataDirectory => State.Paths.DataDirectory;
    public string VersionText => $"FCP {typeof(SettingsPageViewModel).Assembly.GetName().Version?.ToString(3)}";

    [ObservableProperty]
    private bool _isolateModdedByDefault = true;

    [ObservableProperty]
    private bool _autoChineseLanguage = true;

    [ObservableProperty]
    private LauncherVisibility _visibility = LauncherVisibility.HideAndRestore;

    [ObservableProperty]
    private DownloadSource _downloadSource = DownloadSource.Auto;

    [ObservableProperty]
    private GamePriority _priority = GamePriority.Normal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPcl))]
    private string? _pclImportText;

    public bool HasPcl => State.HasPcl || PclImportText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMemoryCustom))]
    private bool _isMemoryAuto = true;

    public bool IsMemoryCustom
    {
        get => !IsMemoryAuto;
        set => IsMemoryAuto = !value;
    }

    public double TotalMemoryGb => _system.TotalPhysicalMemoryMb / 1024.0;

    public double UsedMemoryGb => (_system.TotalPhysicalMemoryMb - _system.AvailablePhysicalMemoryMb) / 1024.0;

    public double MaxMemoryGb => Math.Max(2, Math.Floor(TotalMemoryGb));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryUsedRatio), nameof(MemoryGameRatio), nameof(MemoryFreeRatio), nameof(MemoryFreeGb))]
    private double _memoryGb = 4;

    public double MemoryUsedRatio => UsedMemoryGb / TotalMemoryGb;

    public double MemoryGameRatio => Math.Min(MemoryGb, Math.Max(0, TotalMemoryGb - UsedMemoryGb)) / TotalMemoryGb;

    public double MemoryFreeRatio => Math.Max(0, 1 - MemoryUsedRatio - MemoryGameRatio);

    public double MemoryFreeGb => Math.Max(0, TotalMemoryGb - UsedMemoryGb - MemoryGb);

    [ObservableProperty]
    private bool _isScanningJava;

    /// <summary>重新读取设置（进入设置页时调用）。</summary>
    public Task ReloadAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _settings = await _store.LoadAsync();
            IsolateModdedByDefault = _settings.Isolation != IsolationPolicy.Off;
            AutoChineseLanguage = _settings.AutoChineseLanguage;
            Visibility = _settings.Visibility;
            DownloadSource = _settings.DownloadSource;
            Priority = _settings.Priority;
            PclImportText = _settings.PclImport is { } import ? $"上次导入：{import.ImportedAt:yyyy-MM-dd HH:mm}，来自 {import.SourceDirectory}" : null;
            IsMemoryAuto = _settings.Memory.Mode == MemoryMode.Auto;
            MemoryGb = Math.Round(_settings.Memory.CustomMb / 1024.0 * 2) / 2;
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnIsolateModdedByDefaultChanged(bool value) => Save(s => s.Isolation = value ? IsolationPolicy.ModdedAndNonRelease : IsolationPolicy.Off);

    partial void OnAutoChineseLanguageChanged(bool value) => Save(s => s.AutoChineseLanguage = value);

    partial void OnVisibilityChanged(LauncherVisibility value) => Save(s => s.Visibility = value);

    partial void OnDownloadSourceChanged(DownloadSource value) => Save(s => s.DownloadSource = value);

    partial void OnPriorityChanged(GamePriority value) => Save(s => s.Priority = value);

    /// <summary>重新从 PCL 导入全局设置（覆盖前先确认）。</summary>
    [RelayCommand]
    private async Task ReimportFromPclAsync()
    {
        var confirm = new MessageDialogViewModel(
            "重新从 PCL 导入设置？",
            "将重新读取 PCL 的全局设置（内存、版本隔离、启动器可见性、进程优先级、游戏窗口、参数等），覆盖 FCP 中对应的设置。每个版本的设置不受影响，PCL 的文件不会被修改。",
            buttons: [new DialogButton("取消", false), new DialogButton("重新导入", true)]);
        if (!await _shell.ShowDialogAsync(confirm))
            return;
        try
        {
            var record = await State.ReimportFromPclAsync();
            await LoadAsync();
            _shell.ShowHint(record is null ? "没有找到 PCL 的设置" : $"已重新导入 {record.Items.Count} 项设置", record is null ? HintKind.Error : HintKind.Success);
        }
        catch (Exception ex)
        {
            _shell.ShowError(ex);
        }
    }

    partial void OnIsMemoryAutoChanged(bool value) => Save(s => s.Memory = s.Memory with { Mode = value ? MemoryMode.Auto : MemoryMode.Custom });

    partial void OnMemoryGbChanged(double value) => Save(s => s.Memory = s.Memory with { CustomMb = (int)(value * 1024) });

    private void Save(Action<GlobalSettings> change)
    {
        if (_loading || _settings is null)
            return;
        change(_settings);
        _ = _store.SaveAsync(_settings);
    }

    [RelayCommand]
    private void OpenConfigFolder() => _platformShell.OpenFolder(State.Paths.ConfigDirectory);

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(State.Paths.DataDirectory);
        _platformShell.OpenFolder(State.Paths.DataDirectory);
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(State.Paths.LogsDirectory);
        _platformShell.OpenFolder(State.Paths.LogsDirectory);
    }

    [RelayCommand]
    private void OpenJavaFolder(JavaRuntime java) => _platformShell.OpenFolder(java.Home);

    [RelayCommand]
    private async Task RescanJavaAsync()
    {
        IsScanningJava = true;
        try
        {
            var list = await Task.Run(() => _javaManager.RefreshAsync());
            State.Javas = list;
            _shell.ShowHint($"找到 {list.Count} 个 Java", HintKind.Success);
        }
        finally
        {
            IsScanningJava = false;
        }
    }

    [RelayCommand]
    private async Task AddJavaAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择 java.exe", Filter = "java.exe|java.exe" };
        if (dialog.ShowDialog() != true)
            return;
        var runtime = await _javaManager.AddUserJavaAsync(dialog.FileName);
        if (runtime is null)
        {
            _shell.ShowHint("这个 Java 无法使用", HintKind.Error);
            return;
        }
        State.Javas = _javaManager.Current;
        _shell.ShowHint($"已添加 {runtime.DisplayName}", HintKind.Success);
    }
}
