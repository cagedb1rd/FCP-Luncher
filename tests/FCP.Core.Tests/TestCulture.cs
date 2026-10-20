using System.Globalization;
using System.Runtime.CompilerServices;

namespace FCP.Core.Tests;

/// <summary>
/// 设置环境变量 FCP_TEST_CULTURE（例如 en-US）后，用指定的语言环境运行测试，
/// 用来在中文系统上复现 CI（英文系统）上的行为。
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("FCP_TEST_CULTURE") is { Length: > 0 } name)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
