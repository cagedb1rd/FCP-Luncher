using System.Net;
using FCP.Core.Net;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FCP.Core.Tests.Net;

public class MirrorMapTests
{
    [Theory]
    [InlineData("https://piston-data.mojang.com/v1/objects/abc/client.jar", "https://bmclapi2.bangbang93.com/v1/objects/abc/client.jar")]
    [InlineData("https://libraries.minecraft.net/com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar", "https://bmclapi2.bangbang93.com/maven/com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar")]
    [InlineData("http://resources.download.minecraft.net/ab/abcdef", "https://bmclapi2.bangbang93.com/assets/ab/abcdef")]
    [InlineData("https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.10/x.jar", "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.20.1-47.4.10/x.jar")]
    [InlineData("https://meta.fabricmc.net/v2/versions/loader", "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader")]
    public void MapsOfficialUrls(string official, string mirror) => MirrorMap.ToMirror(new Uri(official))!.AbsoluteUri.ShouldBe(mirror);

    [Theory]
    [InlineData("https://example.com/file.jar")]
    [InlineData("https://littleskin.cn/api/yggdrasil")]
    public void LeavesOtherUrlsAlone(string url) => MirrorMap.ToMirror(new Uri(url)).ShouldBeNull();
}

public class MirrorHandlerTests : IDisposable
{
    private const string Official = "https://libraries.minecraft.net/a/b/c.jar";
    private const string Mirror = "https://bmclapi2.bangbang93.com/maven/a/b/c.jar";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>假的网络：记录请求顺序，按地址返回结果。</summary>
    private sealed class FakeNetwork(Func<Uri, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(request.RequestUri!.AbsoluteUri);
            return respond(request.RequestUri!, cancellationToken);
        }
    }

    private static Task<HttpResponseMessage> Ok(string body = "ok") => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    private static Task<HttpResponseMessage> Status(HttpStatusCode code) => Task.FromResult(new HttpResponseMessage(code));

    private async Task<HttpClient> ClientAsync(DownloadSource source, FakeNetwork network, TimeSpan? timeout = null)
    {
        var store = new SettingsStore(new AppPaths(_dir, _dir, _dir, false));
        await store.SaveAsync(new GlobalSettings { DownloadSource = source });
        return new HttpClient(new MirrorHandler(store, NullLogger<MirrorHandler>.Instance)
        {
            InnerHandler = network,
            PrimaryHeaderTimeout = timeout ?? TimeSpan.FromSeconds(5),
        });
    }

    [Fact]
    public async Task OfficialFails_FallsBackToMirror_AndPrefersMirrorAfterwards()
    {
        var network = new FakeNetwork((uri, _) => uri.Host.StartsWith("bmclapi") ? Ok("mirror") : Status(HttpStatusCode.ServiceUnavailable));
        using var client = await ClientAsync(DownloadSource.Official, network);

        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("mirror");
        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("mirror");

        network.Requests.ShouldBe([Official, Mirror, Mirror]);
    }

    [Fact]
    public async Task MirrorMissingFile_FallsBackToOfficial()
    {
        var network = new FakeNetwork((uri, _) => uri.Host.StartsWith("bmclapi") ? Status(HttpStatusCode.NotFound) : Ok("official"));
        using var client = await ClientAsync(DownloadSource.Mirror, network);

        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("official");
        network.Requests.ShouldBe([Mirror, Official]);
    }

    [Fact]
    public async Task HangingSource_TimesOut_AndFallsBack()
    {
        var network = new FakeNetwork(async (uri, ct) =>
        {
            if (!uri.Host.StartsWith("bmclapi"))
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return await Ok("mirror");
        });
        using var client = await ClientAsync(DownloadSource.Official, network, TimeSpan.FromMilliseconds(200));

        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("mirror");
    }

    [Fact]
    public async Task BothFail_ReturnsTheError()
    {
        var network = new FakeNetwork((_, _) => Status(HttpStatusCode.BadGateway));
        using var client = await ClientAsync(DownloadSource.Official, network);

        using var response = await client.GetAsync(Official, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task OtherHosts_AreNotTouched()
    {
        var network = new FakeNetwork((_, _) => Ok());
        using var client = await ClientAsync(DownloadSource.Mirror, network);

        await client.GetStringAsync("https://littleskin.cn/api/yggdrasil", TestContext.Current.CancellationToken);
        network.Requests.ShouldBe(["https://littleskin.cn/api/yggdrasil"]);
    }

    [Fact]
    public async Task Auto_ProbesOnce_AndUsesTheWorkingSource()
    {
        // 镜像源不可用（与开发机上的实际情况相同）：测速后应使用官方源，且只测速一次
        var network = new FakeNetwork((uri, _) => uri.Host.StartsWith("bmclapi") ? Status(HttpStatusCode.GatewayTimeout) : Ok("official"));
        using var client = await ClientAsync(DownloadSource.Auto, network);

        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("official");
        (await client.GetStringAsync(Official, TestContext.Current.CancellationToken)).ShouldBe("official");

        network.Requests.Count(r => r.Contains("brigadier")).ShouldBe(2); // 官方 + 镜像各测一次
        network.Requests.Where(r => r.EndsWith("/a/b/c.jar")).ShouldBe([Official, Official]);
    }
}
