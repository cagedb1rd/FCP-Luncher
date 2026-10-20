using System.Text.Json.Nodes;
using FCP.Core.Accounts;
using FCP.Core.Instances;

namespace FCP.Core.Migration;

public enum AccountClueKind
{
    /// <summary>PCL 的离线 UUID 算法（可以直接创建同 UUID 的离线账号）。</summary>
    PclOffline,
    /// <summary>标准离线 UUID（官方启动器、HMCL 等）。</summary>
    StandardOffline,
    /// <summary>版本绑定了皮肤站（authlib-injector），UUID 属于皮肤站的某个角色。</summary>
    SkinSite,
    /// <summary>其他 UUID：很可能是正版账号。</summary>
    LikelyPremium,
}

/// <summary>在某个版本的游戏目录中玩过的一个身份（docs/02 §7、docs/06 §4）。</summary>
public sealed record AccountClue(
    string InstanceName,
    string PlayerName,
    Guid Uuid,
    AccountClueKind Kind,
    DateTimeOffset? LastSeen,
    string? ServerName = null,
    string? ServerUrl = null);

/// <summary>从游戏目录中的明文线索（usercache.json）判断“这个版本里谁玩过、用的是哪种账号”。不读取任何加密数据。</summary>
public static class AccountClues
{
    public static IReadOnlyList<AccountClue> Collect(InstanceEntry entry)
    {
        var path = Path.Combine(entry.GameDirectory, "usercache.json");
        if (!File.Exists(path))
            return [];
        JsonArray? cache;
        try
        {
            cache = JsonNode.Parse(File.ReadAllText(path))?.AsArray();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }

        var clues = new List<AccountClue>();
        foreach (var item in cache ?? [])
        {
            var name = item?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name) || !Guid.TryParse(item!["uuid"]?.GetValue<string>(), out var uuid))
                continue;
            // usercache 的有效期是最后一次使用后的一个月
            DateTimeOffset? lastSeen = DateTimeOffset.TryParse(item["expiresOn"]?.GetValue<string>(), out var expires) ? expires.AddMonths(-1) : null;
            clues.Add(Classify(entry, name, uuid, lastSeen));
        }
        return clues.OrderByDescending(c => c.LastSeen ?? DateTimeOffset.MinValue).ToList();
    }

    public static AccountClue Classify(InstanceEntry entry, string name, Guid uuid, DateTimeOffset? lastSeen)
    {
        var instance = entry.Instance.Name;
        if (uuid == OfflineUuid.PclLegacy(name) || uuid == OfflineUuid.PclLegacy(name, SkinModel.Steve) || uuid == OfflineUuid.PclLegacy(name, SkinModel.Alex))
            return new AccountClue(instance, name, uuid, AccountClueKind.PclOffline, lastSeen);
        if (uuid == OfflineUuid.Standard(name))
            return new AccountClue(instance, name, uuid, AccountClueKind.StandardOffline, lastSeen);
        if (entry.Pcl is { LoginRequirement: 4, AuthServer: { } server } pcl)
            return new AccountClue(instance, name, uuid, AccountClueKind.SkinSite, lastSeen, ServerDisplayName(pcl.AuthServerName, server), server);
        return new AccountClue(instance, name, uuid, AccountClueKind.LikelyPremium, lastSeen);
    }

    /// <summary>“LittleSkin 登录” → “LittleSkin”；没有名称时用域名。</summary>
    public static string ServerDisplayName(string? name, string url)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Replace(" 登录", "", StringComparison.Ordinal).Replace("登录", "", StringComparison.Ordinal).Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }
}
