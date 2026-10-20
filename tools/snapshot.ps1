# 界面截图：构建 FCP.App（Debug），以截图模式运行（窗口在屏幕外渲染，不截取屏幕），
# 依次打开各个页面并保存 PNG 到 artifacts\snapshots。
# 用法：.\tools\snapshot.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot '..\artifacts\snapshots'))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-env.ps1') | Out-Null
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

dotnet build (Join-Path $root 'src\FCP.App') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
$env:FCP_SNAPSHOT_DIR = $OutDir
try {
    $exe = Join-Path $root 'src\FCP.App\bin\Debug\net10.0-windows\win-x64\FCP.exe'
    $process = Start-Process $exe -PassThru
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()   # 只结束我们自己启动的进程
        throw '截图超时（60 秒）'
    }
}
finally {
    Remove-Item Env:\FCP_SNAPSHOT_DIR -ErrorAction SilentlyContinue
}
Get-ChildItem $OutDir | ForEach-Object { $_.FullName }
