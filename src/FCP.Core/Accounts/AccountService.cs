using System.Text.RegularExpressions;
using FCP.Core.Launch;
using FCP.Core.Settings;
using FCP.Core.Storage;

namespace FCP.Core.Accounts;

public enum AccountKind
{
    Offline,
    Microsoft,
    Yggdrasil,
}

/// <summary>一个账号。令牌类字段只保存 DPAPI 加密后的内容，从不保存密码（D-005）。</summary>
public sealed class AccountRecord
{
    public required string Id { get; set; }
    public required AccountKind Kind { get; set; }
    public required string PlayerName { get; set; }

    /// <summary>32 位十六进制 UUID（无连字符）。</summary>
    public required string Uuid { get; set; }

    /// <summary>离线账号的 UUID 方案（从 PCL 迁移来的账号为 PclLegacy，以保持存档进度）。</summary>
    public UuidScheme? Scheme { get; set; }

    /// <summary>第三方登录的 Yggdrasil API 地址与名称。</summary>
    public string? ServerUrl { get; set; }
    public string? ServerName { get; set; }

    public string? ProtectedTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public string KindText => Kind switch
    {
        AccountKind.Offline => "离线账号",
        AccountKind.Microsoft => "正版账号",
        _ => ServerName ?? "第三方账号",
    };
}

public sealed class AccountsFile
{
    public List<AccountRecord> Accounts { get; set; } = [];
}

/// <summary>账号管理（P1：离线账号；P2 加入微软与 Yggdrasil）。</summary>
public sealed partial class AccountService(AppPaths paths, SettingsStore settingsStore)
{
    public async Task<IReadOnlyList<AccountRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Accounts;

    public async Task<AccountRecord?> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        var accounts = (await LoadAsync(cancellationToken).ConfigureAwait(false)).Accounts;
        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return accounts.FirstOrDefault(a => a.Id == settings.SelectedAccountId) ?? accounts.FirstOrDefault();
    }

    /// <summary>添加离线账号并设为当前账号；同名同方案的账号已存在时直接选中它。</summary>
    public async Task<AccountRecord> AddOfflineAsync(string playerName, UuidScheme scheme = UuidScheme.Standard, CancellationToken cancellationToken = default)
    {
        if (ValidateOfflineName(playerName) is { } error)
            throw new UserFacingException("玩家名不可用", error);
        var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var uuid = (scheme == UuidScheme.PclLegacy ? OfflineUuid.PclLegacy(playerName) : OfflineUuid.Standard(playerName)).ToString("N");
        var account = file.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Offline && a.Uuid == uuid);
        if (account is null)
        {
            account = new AccountRecord { Id = Guid.NewGuid().ToString("N"), Kind = AccountKind.Offline, PlayerName = playerName, Uuid = uuid, Scheme = scheme };
            file.Accounts.Add(account);
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        await SelectAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return account;
    }

    /// <summary>
    /// 从迁移线索创建离线账号：使用存档中实际出现的 UUID（可能是 PCL 按皮肤类型微调过的变体）。
    /// 已存在相同 UUID 的离线账号时直接返回它；<paramref name="select"/> 为 true 时同时设为当前账号。
    /// </summary>
    public async Task<AccountRecord> AddOfflineWithUuidAsync(string playerName, Guid uuid, UuidScheme scheme, bool select, CancellationToken cancellationToken = default)
    {
        if (ValidateOfflineName(playerName) is { } error)
            throw new UserFacingException("玩家名不可用", error);
        var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var text = uuid.ToString("N");
        var account = file.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Offline && a.Uuid == text);
        if (account is null)
        {
            account = new AccountRecord { Id = Guid.NewGuid().ToString("N"), Kind = AccountKind.Offline, PlayerName = playerName, Uuid = text, Scheme = scheme };
            file.Accounts.Add(account);
            await SaveAsync(file, cancellationToken).ConfigureAwait(false);
        }
        if (select)
            await SelectAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return account;
    }

    public async Task SelectAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        settings.SelectedAccountId = accountId;
        await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
        file.Accounts.RemoveAll(a => a.Id == accountId);
        await SaveAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>生成启动所需的身份信息。</summary>
    public static LaunchSession CreateSession(AccountRecord account) => account.Kind switch
    {
        AccountKind.Offline => LaunchSession.Offline(account.PlayerName, Guid.ParseExact(account.Uuid, "N")),
        _ => throw new UserFacingException("暂不支持该账号类型", "正版登录和第三方登录将在下一阶段推出，请先使用离线账号。"),
    };

    /// <summary>检查离线玩家名，返回错误原因；可用时返回 null。</summary>
    public static string? ValidateOfflineName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "请输入玩家名。";
        if (name.Length > 16)
            return "玩家名最多 16 个字符。";
        if (!OfflineNameRegex().IsMatch(name))
            return "玩家名只能包含字母、数字、下划线和中文。";
        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_一-鿿]+$")]
    private static partial Regex OfflineNameRegex();

    private async Task<AccountsFile> LoadAsync(CancellationToken cancellationToken) =>
        await JsonFile.ReadAsync<AccountsFile>(paths.AccountsFile, cancellationToken).ConfigureAwait(false) ?? new AccountsFile();

    private Task SaveAsync(AccountsFile file, CancellationToken cancellationToken) =>
        JsonFile.WriteAsync(paths.AccountsFile, file, cancellationToken);
}
