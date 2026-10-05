# ============================================================================
#  安装包实机验证：静默安装 -> 检查文件/快捷方式/注册表 -> 启动程序 -> 卸载
#  以 /CURRENTUSER（非管理员）模式安装，避免触发 UAC
#  用法：test-installer.ps1 [-ProductDir 第四版]
# ============================================================================
param([string]$ProductDir = '', [string]$SetupPath = '')

$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'config.ps1')

# 默认测仓库自己构建出来的 dist\setup.exe；
# $ProductDir 用来指向别的成品目录（例如 ..\第四版），两者都不给就用 dist。
if ($SetupPath)     { $SETUP = $SetupPath }
elseif ($ProductDir) { $SETUP = Join-Path (Join-Path $RepoRoot $ProductDir) 'setup.exe' }
else                { $SETUP = Join-Path $DistDir 'setup.exe' }
$BUILD    = $BuildDir
$TESTDIR  = Join-Path $BUILD 'testinstall'
$DESKTOP  = [Environment]::GetFolderPath('Desktop')
$LNK      = Join-Path $DESKTOP 'FrpWin 内网穿透套装.lnk'

if (-not (Test-Path $SETUP)) { Write-Host "找不到安装包：$SETUP" -ForegroundColor Red; Write-Host '先运行 scripts\build.ps1 生成它。' -ForegroundColor Yellow; exit 1 }

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

Write-Host "=== 0. 环境准备 ===" -ForegroundColor Cyan
# 上一轮跑剩的进程会占住 FrpWin.exe，导致安装程序覆盖不了文件（会静默跳过），
# 后面的自检就会跑到旧版程序上、卡在图形界面里。
Get-Process FrpWin,frps,frpc -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Seconds 1
$oldUnins = Join-Path $TESTDIR 'unins000.exe'
if (Test-Path $oldUnins) {
    Start-Process -FilePath $oldUnins -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait | Out-Null
    Start-Sleep -Seconds 3
}
Remove-Item $TESTDIR -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $LNK) { Remove-Item $LNK -Force }
Write-Host "  安装包: $SETUP ($([math]::Round((Get-Item $SETUP).Length/1MB,1)) MB)"
Write-Host "  测试目录: $TESTDIR"
Write-Host "  桌面快捷方式: $LNK"

# 跑一次外部命令并等它退出。
# 只返回「有没有在超时前跑完」——Start-Process -PassThru 拿到的对象有时候读不到
# ExitCode（.NET 的已知脾气），所以不去依赖它，改成看输出文件。
function Invoke-Exe([string]$Exe, [string[]]$Arguments, [string]$OutFile, [int]$TimeoutSec = 40) {
    Remove-Item $OutFile -Force -ErrorAction SilentlyContinue
    Remove-Item ($OutFile + '.err') -Force -ErrorAction SilentlyContinue
    $proc = $null
    try {
        $proc = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru `
                             -RedirectStandardOutput $OutFile -RedirectStandardError ($OutFile + '.err')
    } catch {
        Write-Host "      启动失败: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
    if ($null -eq $proc) { Write-Host "      没有拿到进程对象" -ForegroundColor Red; return $false }
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        try { $proc.Kill() } catch { }
        Write-Host "      $([IO.Path]::GetFileName($Exe)) 超过 $TimeoutSec 秒没有退出（多半是跑到了不支持该参数的老版本上）" -ForegroundColor Red
        return $false
    }
    return $true
}

Write-Host "`n=== 1. 静默安装 (/CURRENTUSER) ===" -ForegroundColor Cyan
$p = Start-Process -FilePath $SETUP -ArgumentList @(
    '/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
    '/MERGETASKS="desktopicon"',
    "/DIR=`"$TESTDIR`"",
    "/LOG=`"$BUILD\install.log`""
) -Wait -PassThru
Write-Host "  安装程序退出码: $($p.ExitCode)"
Check "安装程序退出码为 0" ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"

Write-Host "`n=== 2. 检查安装的文件 ===" -ForegroundColor Cyan
$expect = @('FrpWin.exe','frps.exe','frpc.exe','使用说明.txt','unins000.exe')
foreach ($f in $expect) {
    $full = Join-Path $TESTDIR $f
    $exists = Test-Path $full
    $size = if ($exists) { "{0:N1} KB" -f ((Get-Item $full).Length/1KB) } else { '-' }
    Check "文件 $f" $exists $size
}

Write-Host "`n=== 3. 检查桌面快捷方式 ===" -ForegroundColor Cyan
Check "桌面快捷方式存在" (Test-Path $LNK) $LNK
if (Test-Path $LNK) {
    $sh = New-Object -ComObject WScript.Shell
    $sc = $sh.CreateShortcut($LNK)
    Write-Host "     TargetPath : $($sc.TargetPath)"
    Write-Host "     WorkingDir : $($sc.WorkingDirectory)"
    Write-Host "     IconLocation: $($sc.IconLocation)"
    Check "快捷方式指向 FrpWin.exe" ($sc.TargetPath -ieq (Join-Path $TESTDIR 'FrpWin.exe'))
    Check "快捷方式有图标" ($sc.IconLocation -match 'FrpWin.exe')
}

Write-Host "`n=== 4. 检查开始菜单 ===" -ForegroundColor Cyan
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'FrpWin'
Check "开始菜单组存在" (Test-Path $startMenu) $startMenu
if (Test-Path $startMenu) {
    Get-ChildItem $startMenu | ForEach-Object { Write-Host "     $($_.Name)" }
}

Write-Host "`n=== 5. 检查卸载注册表项 ===" -ForegroundColor Cyan
$uninstallRoots = @(
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
  'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
)
$found = $null
foreach ($r in $uninstallRoots) {
    if (Test-Path $r) {
        $found = Get-ChildItem $r -ErrorAction SilentlyContinue |
                 Where-Object { $_.GetValue('DisplayName') -like '*FrpWin*' } | Select-Object -First 1
        if ($found) { Write-Host "     位于: $($found.Name)"; break }
    }
}
Check "已注册卸载信息" ($null -ne $found)
if ($found) {
    Write-Host "     DisplayName    : $($found.GetValue('DisplayName'))"
    Write-Host "     DisplayVersion : $($found.GetValue('DisplayVersion'))"
    Write-Host "     UninstallString: $($found.GetValue('UninstallString'))"
}

Write-Host "`n=== 6. 检查数据目录 ===" -ForegroundColor Cyan
$dataDir = Join-Path $env:ProgramData 'FrpWin'
Check "数据目录已创建" (Test-Path $dataDir) $dataDir
if (Test-Path $dataDir) {
    Get-ChildItem $dataDir | ForEach-Object { Write-Host "     $($_.Name)" }
}

Write-Host "`n=== 7. 启动图形管理器 ===" -ForegroundColor Cyan
$app = Start-Process -FilePath (Join-Path $TESTDIR 'FrpWin.exe') -PassThru
Start-Sleep -Seconds 6
$alive = -not $app.HasExited
Check "FrpWin.exe 能正常启动并保持运行" $alive "PID=$($app.Id)"
if (-not $alive) { Write-Host "     退出码: $($app.ExitCode)" }

# 检查主窗口是否真的创建了
if ($alive) {
    $app.Refresh()
    $title = $app.MainWindowTitle
    Write-Host "     主窗口标题: $title"
    Check "主窗口已创建" ($title -like '*FrpWin*') $title
}

# 再启动第二个实例，验证单实例保护（应立即退出并把已有窗口拉到前台）
$app2 = Start-Process -FilePath (Join-Path $TESTDIR 'FrpWin.exe') -PassThru
$exited = $app2.WaitForExit(8000)
Start-Sleep -Milliseconds 300
$app2.Refresh()
$secondExited = $app2.HasExited
Write-Host "     第二个实例是否已自动退出: $secondExited"
Check "单实例保护生效" $secondExited

if ($alive) { $app.Kill(); Start-Sleep -Seconds 1 }
if (-not $app2.HasExited) { $app2.Kill() }

Write-Host "`n=== 8. 执行卸载 ===" -ForegroundColor Cyan
$unins = Join-Path $TESTDIR 'unins000.exe'
if (Test-Path $unins) {
    $u = Start-Process -FilePath $unins -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru
    $u.WaitForExit(120000) | Out-Null
    Write-Host "  卸载程序退出码: $($u.ExitCode)"
    Start-Sleep -Seconds 3
    Check "安装目录已清理" (-not (Test-Path (Join-Path $TESTDIR 'FrpWin.exe'))) "FrpWin.exe 是否还存在: $(Test-Path (Join-Path $TESTDIR 'FrpWin.exe'))"
    Check "桌面快捷方式已删除" (-not (Test-Path $LNK))
    Check "开始菜单已删除" (-not (Test-Path $startMenu))
} else {
    Check "找到卸载程序 unins000.exe" $false
}

# ============================================================================
#  第 9 节：安装时选的语言必须被记下来（这正是“选英文但界面还是中文”的根因）
# ============================================================================
$LANGFILE = Join-Path $env:ProgramData 'FrpWin\language.txt'
$hadLangFile = Test-Path $LANGFILE
$langFileBackup = Join-Path $BUILD 'language.txt.bak'
if ($hadLangFile) { Copy-Item $LANGFILE $langFileBackup -Force }

Write-Host "`n=== 9. 中文安装：说明书是中文，语言记成 zh ===" -ForegroundColor Cyan
Remove-Item $LANGFILE -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\ProgramData\FrpWin\README.en.txt' -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\ProgramData\FrpWin\使用说明.txt' -Force -ErrorAction SilentlyContinue

$p = Start-Process -FilePath $SETUP -ArgumentList @(
    '/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LANG=chinese',
    "/DIR=`"$TESTDIR`"", "/LOG=`"$BUILD\install.log`""
) -Wait -PassThru
Check "中文安装退出码为 0" ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
Check "装的是中文说明书" (Test-Path (Join-Path $TESTDIR '使用说明.txt'))
Check "中文安装不装英文说明书" (-not (Test-Path (Join-Path $TESTDIR 'README.en.txt')))
Check "language.txt 写成了 zh" ((Test-Path $LANGFILE) -and ((Get-Content $LANGFILE -Raw).Trim() -eq 'zh')) "内容=[$(if (Test-Path $LANGFILE) { (Get-Content $LANGFILE -Raw).Trim() })]"
$zhDump = Join-Path $TESTDIR 'lang-zh.txt'
$zhOk = Invoke-Exe (Join-Path $TESTDIR 'FrpWin.exe') @('--dump-texts','auto') $zhDump 40
Check "中文安装的程序能响应 --dump-texts（说明装上的是新版）" $zhOk "输出 $((Get-Item $zhDump).Length) 字节"
$zhLines = @(Get-Content $zhDump -Encoding UTF8)
Check "程序跟着装成中文" ([bool](@($zhLines | Where-Object { $_ -eq 'LANG|zh' }).Count)) "$(@($zhLines | Where-Object { $_ -like 'LANG|*' })[0])"

if (Test-Path (Join-Path $TESTDIR 'unins000.exe')) {
    Start-Process -FilePath (Join-Path $TESTDIR 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait | Out-Null
    Start-Sleep -Seconds 3
}

Write-Host "`n=== 10. 英文安装：说明书是英文，语言记成 en，程序界面全英文 ===" -ForegroundColor Cyan
$p = Start-Process -FilePath $SETUP -ArgumentList @(
    '/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LANG=english',
    "/DIR=`"$TESTDIR`"", "/LOG=`"$BUILD\install.log`""
) -Wait -PassThru
Check "英文安装退出码为 0" ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
Check "装的是英文说明书" (Test-Path (Join-Path $TESTDIR 'README.en.txt'))
Check "英文安装不装中文说明书" (-not (Test-Path (Join-Path $TESTDIR '使用说明.txt')))
Check "language.txt 写成了 en" ((Test-Path $LANGFILE) -and ((Get-Content $LANGFILE -Raw).Trim() -eq 'en')) "内容=[$(if (Test-Path $LANGFILE) { (Get-Content $LANGFILE -Raw).Trim() })]"

$enDump = Join-Path $TESTDIR 'lang-en.txt'
$enOk = Invoke-Exe (Join-Path $TESTDIR 'FrpWin.exe') @('--dump-texts','auto') $enDump 40
Check "英文安装的程序能响应 --dump-texts" $enOk "输出 $((Get-Item $enDump).Length) 字节"
$enLines = @(Get-Content $enDump -Encoding UTF8)
Check "程序跟着装成英文" ([bool](@($enLines | Where-Object { $_ -eq 'LANG|en' }).Count)) "$(@($enLines | Where-Object { $_ -like 'LANG|*' })[0])"
$enLeftover = @($enLines | Where-Object {
    $_ -match '[\u4e00-\u9fff]' -and $_ -notmatch '\|langCombo\|' -and $_ -notmatch '\|connList\|'
})
Check "英文安装后界面里没有中文" ($enLeftover.Count -eq 0) $(if ($enLeftover.Count) { ($enLeftover | Select-Object -First 2) -join ' ;; ' } else { "共检查 $($enLines.Count) 行" })

if (Test-Path (Join-Path $TESTDIR 'unins000.exe')) {
    Start-Process -FilePath (Join-Path $TESTDIR 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait | Out-Null
    Start-Sleep -Seconds 3
    Check "英文安装也能正常卸载" (-not (Test-Path (Join-Path $TESTDIR 'FrpWin.exe')))
}

# 收尾：把安装期间写下的 language.txt 还原，免得影响别的测试和用户自己的设置
if ($hadLangFile) { Copy-Item $langFileBackup $LANGFILE -Force } else { Remove-Item $LANGFILE -Force -ErrorAction SilentlyContinue }

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
