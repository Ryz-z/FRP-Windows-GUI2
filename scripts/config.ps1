# ============================================================================
#  公共路径与参数（其它脚本都点这个文件）
#  所有路径都基于仓库自身位置推导，clone 到任何目录都能直接用。
# ============================================================================

$RepoRoot   = Split-Path $PSScriptRoot -Parent
$SrcDir     = Join-Path $RepoRoot 'src'
$InstallerDir = Join-Path $RepoRoot 'installer'
$DocsDir    = Join-Path $RepoRoot 'docs'
$I18nDir    = Join-Path $PSScriptRoot 'i18n'

# 第三方 frp 源码与编译产物（不纳入版本控制，由 fetch-frp.ps1 生成）
$ThirdPartyDir = Join-Path $RepoRoot 'third_party'
$FrpVersion    = '0.71.0'
$FrpSrcDir     = Join-Path $ThirdPartyDir "frp-$FrpVersion"
$FrpBinDir     = Join-Path $FrpSrcDir 'bin'

# 本项目的构建中间产物与最终产物
$BuildDir   = Join-Path $RepoRoot 'build'
$StageDir   = Join-Path $BuildDir 'staging'
$OutputDir  = Join-Path $BuildDir 'output'
$StageSrcDir = Join-Path $BuildDir 'staging-src'
$DistDir    = Join-Path $RepoRoot 'dist'

# 图形管理器编译输出
$GuiProj    = Join-Path $SrcDir 'FrpWin\FrpWin.csproj'
$GuiExe     = Join-Path $SrcDir 'FrpWin\bin\Release\net48\FrpWin.exe'

# 安装包信息
$AppVersion = '1.0.0'
$SetupName  = "FrpWin-Setup-$AppVersion.exe"

# 带中文的文件必须存成 UTF-8 with BOM，
# 否则 PowerShell 5.1 会按 GBK 解析导致语法错误、Inno Setup 会显示乱码。
function Add-Utf8Bom([string]$Path) {
    if (-not (Test-Path $Path)) { return }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { return }
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    [System.IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding $true))
}

# 定位 Inno Setup 编译器
function Find-ISCC {
    $cands = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { return $c } }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# 定位 Go 工具链（优先仓库内 tools\go，其次 PATH）
function Find-Go {
    $local = Join-Path $RepoRoot 'tools\go\bin\go.exe'
    if (Test-Path $local) { return $local }
    $cmd = Get-Command go.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
#  调用外部程序（curl / npm / go / dotnet / ISCC ...）
#
#  Windows PowerShell 5.1 有个坑：当 $ErrorActionPreference='Stop' 时，
#  外部程序往 stderr 写的任何一行（哪怕只是 curl 的下载进度条）都会被当成
#  致命错误并中断整个脚本。所以统一走这个函数：临时降级错误策略、合并 stderr，
#  只看真正的退出码。
# ---------------------------------------------------------------------------
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [string[]]$Arguments = @(),
        [switch]$Quiet
    )
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $File @Arguments 2>&1
        $code = $LASTEXITCODE
        if (-not $Quiet -and $out) {
            $out | ForEach-Object { Write-Host "  $_" }
        }
        return $code
    } finally {
        $ErrorActionPreference = $prev
    }
}
