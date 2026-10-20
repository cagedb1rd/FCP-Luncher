using CommunityToolkit.Mvvm.ComponentModel;

namespace FCP.App.ViewModels;

public enum SettingsSection
{
    Launch,
    Personalization,
    Download,
    Other,
}

public sealed record JavaEntry(string Title, string Path, string Source);

/// <summary>设置页（原型）：左侧分页，右侧内容。</summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    public SettingsPageViewModel(ShellViewModel shell)
    {
        _ = shell;
        Javas =
        [
            new JavaEntry("Java 17.0.10 · Oracle", @"C:\Program Files\Java\jdk-17\bin\java.exe", "注册表"),
            new JavaEntry("Java 21.0.6 · JetBrains", @"D:\pycharm\PyCharm Community Edition 2024.3.4\jbr\bin\java.exe", "PCL 记录"),
            new JavaEntry("Java 25.0.2 · Eclipse Adoptium", @"C:\Program Files\Eclipse Adoptium\jdk-25.0.2.10-hotspot\bin\java.exe", "常见目录"),
        ];
    }

    public IReadOnlyList<JavaEntry> Javas { get; }

    [ObservableProperty]
    private SettingsSection _section = SettingsSection.Launch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMemoryCustom))]
    private bool _isMemoryAuto = true;

    public bool IsMemoryCustom
    {
        get => !IsMemoryAuto;
        set => IsMemoryAuto = !value;
    }

    [ObservableProperty]
    private double _memoryGb = 6;
}
