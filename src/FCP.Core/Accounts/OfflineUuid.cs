using System.Security.Cryptography;
using System.Text;

namespace FCP.Core.Accounts;

/// <summary>离线账号使用的 UUID 生成方案。</summary>
public enum UuidScheme
{
    /// <summary>标准方案：UUID v3(MD5("OfflinePlayer:" + 玩家名))，与原版服务端、官方启动器、HMCL 一致。</summary>
    Standard,

    /// <summary>与 PCL 兼容的方案，从 PCL 迁移来的离线账号使用它，以保持存档进度。</summary>
    PclLegacy,

    /// <summary>用户手动指定的 UUID。</summary>
    Custom,
}

/// <summary>离线皮肤模型（决定默认皮肤是 Steve 还是 Alex）。</summary>
public enum SkinModel
{
    Steve,
    Alex,
}

/// <summary>
/// 离线 UUID 的计算。PCL 方案的格式事实见 docs/02-pcl-data-formats.md §6。
/// </summary>
public static class OfflineUuid
{
    /// <summary>标准离线 UUID（UUID v3）。</summary>
    public static Guid Standard(string playerName)
    {
        ArgumentNullException.ThrowIfNull(playerName);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // 版本 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 变体
        return new Guid(hash, bigEndian: true);
    }

    /// <summary>与 PCL 兼容的离线 UUID（不做皮肤模型调整）。</summary>
    public static Guid PclLegacy(string playerName) => Guid.ParseExact(PclLegacyHex(playerName), "N");

    /// <summary>
    /// 与 PCL 兼容的离线 UUID，并按照 PCL 在“离线皮肤 = Steve / Alex / 自定义”时的做法调整，
    /// 使默认皮肤符合 <paramref name="model"/>。
    /// </summary>
    public static Guid PclLegacy(string playerName, SkinModel model)
    {
        var hex = PclLegacyHex(playerName);
        // 每次把最后 5 位十六进制数加 1（到 FFFFF 后回绕），直到默认皮肤符合要求。
        // 由于加 1 一定会翻转最后一位的奇偶性，这个循环最多执行 2 次。
        while (DefaultSkinModel(hex) != model)
        {
            var tail = Convert.ToInt64(hex[27..], 16);
            tail = tail == 0xFFFFF ? 1 : tail + 1;
            hex = hex[..27] + tail.ToString("X5");
        }
        return Guid.ParseExact(hex, "N");
    }

    /// <summary>
    /// PCL 判断默认皮肤的方法：取 32 位十六进制 UUID 的第 7、15、23、31 位做异或，奇数为 Alex。
    /// </summary>
    public static SkinModel DefaultSkinModel(Guid uuid) => DefaultSkinModel(uuid.ToString("N"));

    /// <summary>
    /// 判断一个 UUID 是否具有 PCL 离线 UUID 的特征（前 16 位只编码了玩家名长度）。
    /// 返回 true 时，<paramref name="nameLength"/> 为推断出的玩家名长度。
    /// </summary>
    public static bool LooksLikePclLegacy(Guid uuid, out int nameLength)
    {
        var hex = uuid.ToString("N").ToUpperInvariant();
        nameLength = 0;
        // 前 16 位 = HEX(长度) 左补 0 到 16 位，再把第 12 位替换成 '3'
        if (hex[12] != '3' || hex[16] != '9')
            return false;
        var head = hex[..12] + hex[13..16];
        if (!long.TryParse(head, System.Globalization.NumberStyles.HexNumber, null, out var length))
            return false;
        // 玩家名长度一般为 1~16；放宽到 64，过滤掉随机 UUID
        if (length is < 1 or > 64)
            return false;
        nameLength = (int)length;
        return true;
    }

    internal static string PclLegacyHex(string playerName)
    {
        ArgumentNullException.ThrowIfNull(playerName);
        var full = EnsureLength(playerName.Length.ToString("X"), 16) + EnsureLength(StableHash(playerName).ToString("X"), 16);
        return full[..12] + "3" + full[13..16] + "9" + full[17..32];
    }

    /// <summary>
    /// 跨环境稳定的 64 位字符串哈希。
    /// 算法引用自 MeloongCore（Apache-2.0）的 StringExtensions.GetStableHashCode，见 NOTICE.md。
    /// </summary>
    internal static ulong StableHash(string value)
    {
        ulong result = 5381;
        foreach (var c in value)
            result = (result << 5) ^ result ^ c;
        return result ^ 0xA98F501BC684032FUL;
    }

    // 过短则在左侧补 '0'，过长则截取左侧 length 位
    private static string EnsureLength(string value, int length) =>
        value.Length > length ? value[..length] : value.PadLeft(length, '0');

    private static SkinModel DefaultSkinModel(string hex32)
    {
        int Digit(int index) => Convert.ToInt32(hex32[index].ToString(), 16);
        return ((Digit(7) ^ Digit(15) ^ Digit(23) ^ Digit(31)) & 1) == 1 ? SkinModel.Alex : SkinModel.Steve;
    }
}
