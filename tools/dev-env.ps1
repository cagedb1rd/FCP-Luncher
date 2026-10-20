# 开发环境：在当前 PowerShell 会话中使用本仓库要求的 .NET SDK。
# 用法：. .\tools\dev-env.ps1
# 只修改当前进程的环境变量，不改动系统或用户设置。

$dotnetRoot = if ($env:FCP_DOTNET_ROOT) { $env:FCP_DOTNET_ROOT } elseif (Test-Path 'D:\dotnet\dotnet.exe') { 'D:\dotnet' } else { $null }
if ($dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    if (-not ($env:Path -split ';' | Select-Object -First 1).Equals($dotnetRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $env:Path = "$dotnetRoot;$env:Path"
    }
}
if (-not $env:NUGET_PACKAGES -and (Test-Path 'D:\')) { $env:NUGET_PACKAGES = 'D:\dotnet-data\nuget\packages' }
if (-not $env:NUGET_HTTP_CACHE_PATH -and (Test-Path 'D:\')) { $env:NUGET_HTTP_CACHE_PATH = 'D:\dotnet-data\nuget\http-cache' }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Host "dotnet: $((Get-Command dotnet).Source)  SDK: $(dotnet --version)"
