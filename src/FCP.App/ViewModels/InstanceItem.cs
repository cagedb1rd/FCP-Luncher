using CommunityToolkit.Mvvm.ComponentModel;
using FCP.Core.Instances;
using FCP.Core.Launch;
using FCP.UI.Controls;

namespace FCP.App.ViewModels;

/// <summary>界面上的一个版本（包装 <see cref="InstanceEntry"/>，提供显示用的文字和图标）。</summary>
public sealed partial class InstanceItem : ObservableObject
{
    private int? _modCount;

    public InstanceItem(InstanceEntry entry) => _entry = entry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavorite), nameof(FavoriteActionText), nameof(LastPlayedText), nameof(PlayTimeText), nameof(ActivityText))]
    private InstanceEntry _entry;

    public GameInstance Instance => Entry.Instance;
    public string Name => Instance.Name;
    public string McVersion => Instance.MinecraftVersion ?? "未知版本";
    public string? Loader => Instance.PrimaryLoader?.DisplayName;

    /// <summary>第三方登录服务器名（来自 PCL 的版本设置），例如“LittleSkin”。</summary>
    public string? Account => Entry.Pcl?.LoginRequirement == 4 ? (Entry.Pcl.AuthServerName ?? "第三方登录").Replace(" 登录", "", StringComparison.Ordinal) : null;

    public string Info => Instance.Description;
    public string? Description => Entry.Description;
    public bool IsHealthy => Instance.Health == InstanceHealth.Ok;
    public bool IsFavorite => Entry.IsFavorite;
    public string FavoriteActionText => IsFavorite ? "取消收藏" : "收藏";

    public BlockKind Kind => Instance.Health != InstanceHealth.Ok
        ? BlockKind.Error
        : Instance.PrimaryLoader?.Type switch
        {
            LoaderType.Forge => BlockKind.Forge,
            LoaderType.NeoForge => BlockKind.NeoForge,
            LoaderType.Fabric => BlockKind.Fabric,
            LoaderType.Quilt => BlockKind.Quilt,
            LoaderType.OptiFine => BlockKind.OptiFine,
            LoaderType.LiteLoader => BlockKind.Forge,
            _ => Instance.VersionType switch
            {
                GameVersionType.Snapshot => BlockKind.Snapshot,
                GameVersionType.Old => BlockKind.Old,
                GameVersionType.AprilFools => BlockKind.AprilFools,
                _ => BlockKind.Vanilla,
            },
        };

    public int ModCount => _modCount ??= MemoryCalculator.CountMods(Entry.GameDirectory);
    public string ModCountText => ModCount > 0 ? $"{ModCount} 个 Mod" : "无 Mod";

    /// <summary>启用 / 禁用 Mod 后重新统计。</summary>
    public void InvalidateModCount()
    {
        _modCount = null;
        OnPropertyChanged(nameof(ModCount));
        OnPropertyChanged(nameof(ModCountText));
    }

    public string LastPlayedText => Entry.LastPlayed is { } time ? RelativeTime(time) : "从未游玩";

    public string? PlayTimeText => Entry.Meta.TotalPlaySeconds switch
    {
        <= 0 => null,
        < 3600 => $"已玩 {Math.Max(1, Entry.Meta.TotalPlaySeconds / 60)} 分钟",
        var s => $"已玩 {s / 3600.0:0.#} 小时",
    };

    /// <summary>“3 天前 · 已玩 12 小时”。</summary>
    public string ActivityText => PlayTimeText is { } played ? $"{LastPlayedText} · {played}" : LastPlayedText;

    public static string RelativeTime(DateTimeOffset time)
    {
        var span = DateTimeOffset.Now - time;
        return span.TotalMinutes switch
        {
            < 1 => "刚刚",
            < 60 => $"{(int)span.TotalMinutes} 分钟前",
            < 60 * 24 => $"{(int)span.TotalHours} 小时前",
            < 60 * 24 * 2 => "昨天",
            < 60 * 24 * 30 => $"{(int)span.TotalDays} 天前",
            _ => time.Year == DateTimeOffset.Now.Year ? time.ToString("M 月 d 日") : time.ToString("yyyy 年 M 月"),
        };
    }
}
