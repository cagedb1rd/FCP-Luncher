using FCP.Core.Accounts;
using FCP.Core.Folders;
using FCP.Core.Instances;
using FCP.Core.Migration.Pcl;
using FCP.Core.Platform;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Migration;

/// <summary>
/// 第一次打开时从 PCL 迁移（docs/06，产品原则 4）：
/// <list type="number">
/// <item>导入 PCL 的全局设置（内存、版本隔离、启动器可见性、优先级、游戏窗口、参数、GC、下载源）；</item>
/// <item>分析每个版本中玩过的身份：PCL / 标准离线账号直接创建同 UUID 的账号，皮肤站和正版身份记录为“需要你确认”；</item>
/// <item>把结果记录在设置中（只导入一次，之后用户在 FCP 中的修改优先；可以在设置中重新导入）。</item>
/// </list>
/// PCL 的文件和注册表全程只读。
/// </summary>
public sealed class PclMigrationService(
    SettingsStore settingsStore,
    IRegistryReader registry,
    InstanceScanner scanner,
    InstanceCatalog catalog,
    AccountService accounts,
    ILogger<PclMigrationService> logger)
{
    /// <summary>还没有导入过时执行导入；已经导入过时直接返回记录。电脑上没有 PCL 时返回 null。</summary>
    public async Task<PclImportRecord?> RunOnceAsync(GameFolderDiscoveryResult discovery, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.PclImport is { } existing)
            return existing;
        return await ImportAsync(discovery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>（重新）从 PCL 导入，覆盖对应的全局设置。</summary>
    public async Task<PclImportRecord?> ImportAsync(GameFolderDiscoveryResult discovery, CancellationToken cancellationToken = default)
    {
        PclGlobalSettings? pcl = null;
        foreach (var dir in OrderPclDirectories(discovery))
        {
            pcl = await PclGlobalSettingsReader.ReadAsync(dir, registry, cancellationToken).ConfigureAwait(false);
            if (pcl is not null)
                break;
        }
        if (pcl is null)
            return null;

        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var items = PclGlobalSettingsMapper.Apply(pcl, settings).ToList();
        var notes = new List<string>();

        // 账号连续性：扫描所有游戏文件夹中的版本（隔离结果依赖刚导入的全局设置）
        var clues = new List<AccountClue>();
        foreach (var folder in discovery.Folders)
        {
            try
            {
                var entries = await catalog.BuildEntriesAsync(await scanner.ScanAsync(folder.Path, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                foreach (var entry in entries.Where(e => e.Instance.Health == InstanceHealth.Ok))
                    clues.AddRange(AccountClues.Collect(entry));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "迁移时读取游戏文件夹失败：{Path}", folder.Path);
            }
        }
        await ApplyAccountCluesAsync(clues, items, notes, cancellationToken).ConfigureAwait(false);

        var record = new PclImportRecord(DateTimeOffset.Now, pcl.SourceDirectory, items) { Notes = notes };
        settings.PclImport = record;
        await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("已从 PCL 导入设置（{Source}）：{Items}；需要注意：{Notes}", pcl.SourceDirectory, string.Join("；", items), notes.Count == 0 ? "无" : string.Join("；", notes));
        return record;
    }

    private async Task ApplyAccountCluesAsync(List<AccountClue> clues, List<string> items, List<string> notes, CancellationToken cancellationToken)
    {
        var hasAccount = (await accounts.ListAsync(cancellationToken).ConfigureAwait(false)).Count > 0;

        // 离线身份：直接创建同 UUID 的离线账号（最近使用的那个设为当前账号）
        foreach (var clue in clues
                     .Where(c => c.Kind is AccountClueKind.PclOffline or AccountClueKind.StandardOffline)
                     .OrderByDescending(c => c.LastSeen ?? DateTimeOffset.MinValue)
                     .DistinctBy(c => c.Uuid))
        {
            if (AccountService.ValidateOfflineName(clue.PlayerName) is not null)
                continue;
            var scheme = clue.Kind == AccountClueKind.PclOffline ? UuidScheme.PclLegacy : UuidScheme.Standard;
            await accounts.AddOfflineWithUuidAsync(clue.PlayerName, clue.Uuid, scheme, select: !hasAccount, cancellationToken).ConfigureAwait(false);
            hasAccount = true;
            items.Add($"离线账号 {clue.PlayerName}（与 {(scheme == UuidScheme.PclLegacy ? "PCL" : "原来的启动器")} 相同的 UUID，存档进度不变）");
        }

        // 皮肤站与正版身份：需要登录，按版本列出应该选择的角色
        foreach (var group in clues.Where(c => c.Kind == AccountClueKind.SkinSite).GroupBy(c => c.ServerName))
        {
            var roles = group.GroupBy(c => c.InstanceName).Select(g => $"{g.Key}：{g.First().PlayerName}");
            notes.Add($"这些版本使用 {group.Key} 登录，登录后选择原来的角色即可继续存档进度（{string.Join("；", roles)}）");
        }
        foreach (var clue in clues.Where(c => c.Kind == AccountClueKind.LikelyPremium).DistinctBy(c => c.Uuid))
            notes.Add($"“{clue.InstanceName}” 的存档属于正版账号 {clue.PlayerName}，登录这个微软账号即可继续存档进度");
    }

    /// <summary>优先使用上次选中的游戏文件夹所在的 PCL。</summary>
    private static IEnumerable<string> OrderPclDirectories(GameFolderDiscoveryResult discovery) =>
        discovery.PclExecutableDirectories
            .OrderBy(dir => discovery.PclSelectedFolder is { } selected && selected.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
}
