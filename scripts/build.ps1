# ============================================================================
#  一键构建：编译图形管理器 → 暂存 → 打安装包 → 组装 dist 目录
#
#  用法：
#    .\scripts\build.ps1                    完整构建
#    .\scripts\build.ps1 -SkipInstaller     只编译 FrpWin.exe，不打安装包
#
#  前置条件：
#    1) 已运行过 .\scripts\fetch-frp.ps1（生成 frps.exe / frpc.exe）
#    2) 安装 .NET SDK 6+ ；打安装包还需要 Inno Setup 6
# ============================================================================
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'config.ps1')

# ---------------------------------------------------------- 1. 编译图形管理器
Write-Step "1/4  编译图形管理器 (FrpWin.exe)"
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not $dotnet) { throw "找不到 dotnet，请安装 .NET SDK 6 或更高版本" }
Write-Host "  dotnet: $($dotnet.Source)"
if ((Invoke-Native -File $dotnet.Source -Arguments @('build',$GuiProj,'-c','Release','-v','m') -Quiet) -ne 0) {
    throw "FrpWin.exe 编译失败"
}
if (-not (Test-Path $GuiExe)) { throw "没有生成 $GuiExe" }
Write-Host ("  生成: FrpWin.exe  {0:N1} KB" -f ((Get-Item $GuiExe).Length / 1KB))

if ($SkipInstaller) {
    Write-Host "`n已跳过安装包，完成 ✔" -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- 2. 暂存目录
Write-Step "2/4  汇编 staging 目录"
$frps = Join-Path $FrpBinDir 'frps.exe'
$frpc = Join-Path $FrpBinDir 'frpc.exe'
foreach ($f in @($frps, $frpc)) {
    if (-not (Test-Path $f)) {
        throw "缺少 $f`n请先运行: .\scripts\fetch-frp.ps1"
    }
}

Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$StageDir\conf" | Out-Null

$assets = Join-Path $InstallerDir 'assets'
$items = @(
    @{ Src = $GuiExe;                            Dst = "$StageDir\FrpWin.exe";     What = '图形管理器' },
    @{ Src = $frps;                              Dst = "$StageDir\frps.exe";       What = 'frp 服务端' },
    @{ Src = $frpc;                              Dst = "$StageDir\frpc.exe";       What = 'frp 客户端' },
    @{ Src = "$assets\app.ico";                  Dst = "$StageDir\app.ico";        What = '图标' },
    @{ Src = "$DocsDir\使用说明.txt";              Dst = "$StageDir\使用说明.txt";    What = '使用说明(中文)' },
    @{ Src = "$assets\使用说明.en.txt";           Dst = "$StageDir\README.en.txt";  What = '使用说明(英文)' },
    @{ Src = "$assets\conf\frps.toml";           Dst = "$StageDir\conf\frps.toml"; What = '服务端示例配置' },
    @{ Src = "$assets\conf\frpc.toml";           Dst = "$StageDir\conf\frpc.toml"; What = '客户端示例配置' }
)
foreach ($i in $items) {
    if (-not (Test-Path $i.Src)) { throw "缺少文件：$($i.Src)" }
    Copy-Item $i.Src $i.Dst -Force
    Write-Host ("  {0,-16} {1,-16} {2,9:N1} KB" -f $i.What, (Split-Path $i.Dst -Leaf), ((Get-Item $i.Dst).Length / 1KB))
}
foreach ($t in @("$StageDir\使用说明.txt", "$StageDir\README.en.txt", "$StageDir\conf\frps.toml", "$StageDir\conf\frpc.toml")) { Add-Utf8Bom $t }

# ------------------------------------------------------------ 3. 编译安装包
Write-Step "3/4  编译安装包 (Inno Setup)"
$iscc = Find-ISCC
if (-not $iscc) {
    throw "找不到 ISCC.exe，请安装 Inno Setup 6：https://jrsoftware.org/isdl.php"
}
Write-Host "  ISCC: $iscc"

Remove-Item $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$code = Invoke-Native -File $iscc -Arguments @(
    "/DStageDir=$StageDir", "/DOutputDir=$OutputDir", (Join-Path $InstallerDir 'FrpWin.iss'))
if ($code -ne 0) { throw "Inno Setup 编译失败，退出码 $code" }

$setup = Join-Path $OutputDir $SetupName
if (-not (Test-Path $setup)) { throw "未生成安装包：$setup" }
Write-Host ("  生成: {0}  {1:N1} MB" -f $SetupName, ((Get-Item $setup).Length / 1MB))

# --------------------------------------------------------------- 4. dist 目录
Write-Step "4/4  组装 dist 目录"
Remove-Item $DistDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$DistDir\绿色免安装版\conf" | Out-Null

Copy-Item $setup "$DistDir\setup.exe" -Force
Copy-Item "$StageDir\使用说明.txt" "$DistDir\使用说明.txt" -Force
Copy-Item "$StageDir\README.en.txt" "$DistDir\README.en.txt" -Force
foreach ($f in @('FrpWin.exe', 'frps.exe', 'frpc.exe')) {
    Copy-Item "$StageDir\$f" "$DistDir\绿色免安装版\$f" -Force
}
Copy-Item "$StageDir\conf\frps.toml" "$DistDir\绿色免安装版\conf\frps.toml" -Force
Copy-Item "$StageDir\conf\frpc.toml" "$DistDir\绿色免安装版\conf\frpc.toml" -Force

# 版本更新说明（随安装包一起给用户看）
$changelog = Join-Path $StageSrcDir '更新说明-第四版.txt'
if (Test-Path $changelog) {
    Copy-Item $changelog "$DistDir\更新说明.txt" -Force
    Add-Utf8Bom "$DistDir\更新说明.txt"
}

Add-Utf8Bom "$DistDir\使用说明.txt"
Add-Utf8Bom "$DistDir\README.en.txt"

# 哈希清单，方便校验下载是否完整（必须在补完 BOM 之后再算）
$files = Get-ChildItem $DistDir -Recurse -File
$lines = foreach ($f in $files) {
    "$((Get-FileHash $f.FullName -Algorithm SHA256).Hash)  $($f.FullName.Substring($DistDir.Length + 1))"
}
[System.IO.File]::WriteAllText("$DistDir\SHA256校验值.txt", ($lines -join "`r`n") + "`r`n",
    (New-Object System.Text.UTF8Encoding $true))

foreach ($f in (Get-ChildItem $DistDir -Recurse -File)) {
    Write-Host ("  {0,-40} {1,9:N2} MB" -f $f.FullName.Substring($DistDir.Length + 1), ($f.Length / 1MB))
}

Write-Host "`n完成 ✔  产物目录：$DistDir" -ForegroundColor Green
Write-Host "把 dist\ 里的内容作为 GitHub Release 的附件上传即可。" -ForegroundColor DarkGray
