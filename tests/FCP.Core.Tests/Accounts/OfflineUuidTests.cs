using FCP.Core.Accounts;

namespace FCP.Core.Tests.Accounts;

public class OfflineUuidTests
{
    [Fact]
    public void Standard_matches_well_known_value_for_Steve()
    {
        // 广为人知的离线玩家 Steve 的 UUID（原版服务端离线模式同样会得到这个值）
        OfflineUuid.Standard("Steve").ShouldBe(Guid.Parse("5627dd98-e6be-3c21-b8a8-e92344183641"));
    }

    [Theory]
    // 独立实现（Python）计算得到的值；P2 需要再用 PCL 实际离线登录得到的样本核对（docs/07 §4）
    [InlineData("Steve", "0000000000003005998F503099DCF29B")]
    [InlineData("BlockMaster66", "000000000000300D9D375E53E6645ABF")]
    [InlineData("Alex_2024", "00000000000030099B3AB779AC499861")]
    public void PclLegacy_matches_reference_values(string name, string expectedHex)
    {
        OfflineUuid.PclLegacyHex(name).ShouldBe(expectedHex);
        OfflineUuid.PclLegacy(name).ShouldBe(Guid.ParseExact(expectedHex, "N"));
    }

    [Fact]
    public void PclLegacy_handles_non_ascii_names()
    {
        // 中文玩家名：哈希按 UTF-16 码元计算
        OfflineUuid.PclLegacyHex("玩家").ShouldBe("0000000000003002998F501BC6DE5F15");
    }

    [Fact]
    public void PclLegacy_is_different_from_standard()
    {
        OfflineUuid.PclLegacy("Steve").ShouldNotBe(OfflineUuid.Standard("Steve"));
    }

    [Theory]
    [InlineData("Steve")]
    [InlineData("Alex_2024")]
    [InlineData("BlockMaster66")]
    [InlineData("玩家")]
    public void Skin_model_adjustment_produces_requested_model(string name)
    {
        OfflineUuid.DefaultSkinModel(OfflineUuid.PclLegacy(name, SkinModel.Steve)).ShouldBe(SkinModel.Steve);
        OfflineUuid.DefaultSkinModel(OfflineUuid.PclLegacy(name, SkinModel.Alex)).ShouldBe(SkinModel.Alex);
    }

    [Fact]
    public void Skin_model_adjustment_keeps_uuid_when_model_already_matches()
    {
        var plain = OfflineUuid.PclLegacy("Steve");
        OfflineUuid.PclLegacy("Steve", OfflineUuid.DefaultSkinModel(plain)).ShouldBe(plain);
    }

    [Fact]
    public void Detects_pcl_legacy_uuid_and_name_length()
    {
        OfflineUuid.LooksLikePclLegacy(OfflineUuid.PclLegacy("BlockMaster66"), out var length).ShouldBeTrue();
        length.ShouldBe(13);
    }

    [Theory]
    [InlineData("3f2b9c1e-5d47-4a8b-9e21-6c0d8f7a4b55")] // LittleSkin 角色（随机 v4）
    [InlineData("5627dd98-e6be-3c21-b8a8-e92344183641")] // 标准离线 UUID
    public void Does_not_flag_other_uuids_as_pcl_legacy(string uuid)
    {
        OfflineUuid.LooksLikePclLegacy(Guid.Parse(uuid), out _).ShouldBeFalse();
    }
}
