using CommunityToolkit.Mvvm.ComponentModel;

namespace FCP.App.ViewModels;

public enum SettingsSection
{
    Launch,
    Personalization,
    Download,
    Other,
}

public enum LauncherVisibility
{
    KeepOpen,
    Minimize,
    HideAndRestore,
}

public sealed record JavaEntry(string Version, string Vendor, string Path, string Source, bool IsRecommended);

/// <summary>设置页（原型）：左侧分页，右侧内容。</summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    public SettingsPageViewModel(ShellViewModel shell)
    {
        _ = shell;
        Javas =
        [
            new JavaEntry("Java 17.0.10", "Oracle", @"C:\Program Files\Java\jdk-17\bin\java.exe", "注册表", true),
            new JavaEntry("Java 21.0.6", "JetBrains", @"D:\pycharm\PyCharm Community Edition 2024.3.4\jbr\bin\java.exe", "PCL 记录", false),
            new JavaEntry("Java 25.0.2", "Eclipse Adoptium", @"C:\Program Files\Eclipse Adoptium\jdk-25.0.2.10-hotspot\bin\java.exe", "常见目录", false),
        ];
    }

    public IReadOnlyList<JavaEntry> Javas { get; }

    [ObservableProperty]
    private SettingsSection _section = SettingsSection.Launch;

    [ObservableProperty]
    private bool _isolateModdedByDefault = true;

    [ObservableProperty]
    private bool _autoChineseLanguage = true;

    [ObservableProperty]
    private LauncherVisibility _visibility = LauncherVisibility.HideAndRestore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMemoryCustom))]
    private bool _isMemoryAuto = true;

    public bool IsMemoryCustom
    {
        get => !IsMemoryAuto;
        set => IsMemoryAuto = !value;
    }

    // 内存示意（原型数据）：总量、已用、分配给游戏
    public double TotalMemoryGb => 31.8;

    public double UsedMemoryGb => 14.2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryUsedRatio), nameof(MemoryGameRatio), nameof(MemoryFreeRatio), nameof(MemoryFreeGb))]
    private double _memoryGb = 6;

    public double MemoryUsedRatio => UsedMemoryGb / TotalMemoryGb;

    public double MemoryGameRatio => Math.Min(MemoryGb, TotalMemoryGb - UsedMemoryGb) / TotalMemoryGb;

    public double MemoryFreeRatio => Math.Max(0, 1 - MemoryUsedRatio - MemoryGameRatio);

    public double MemoryFreeGb => Math.Max(0, TotalMemoryGb - UsedMemoryGb - MemoryGb);
}
