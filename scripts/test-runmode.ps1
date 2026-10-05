# ============================================================================
#  测试 FrpWin.exe 的前台运行模式 (--run)，它与图形界面“启动”按钮、
#  以及 Windows 服务用的是同一套 FrpRunner 进程管理逻辑
# ============================================================================
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'config.ps1')

$RUN   = Join-Path $BuildDir 'runmode'
$DATA  = Join-Path $env:ProgramData 'FrpWin'
$APP   = "$RUN\FrpWin.exe"
$FRSPS = "$RUN\frps.exe"
$FRPC  = "$RUN\frpc.exe"

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

Write-Host "=== 0. 准备独立运行目录 ===" -ForegroundColor Cyan
Remove-Item $RUN -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $RUN | Out-Null
Copy-Item $GuiExe "$APP" -Force
Copy-Item (Join-Path $FrpBinDir 'frps.exe') $FRSPS -Force
Copy-Item (Join-Path $FrpBinDir 'frpc.exe') $FRPC  -Force
Check "三个 exe 已就位" ((Test-Path $APP) -and (Test-Path $FRSPS) -and (Test-Path $FRPC))
Write-Output "  注意：FrpWin.exe 会从自己所在目录找 frps.exe / frpc.exe"

Write-Host "`n=== 1. 写入界面设置（端口 17100 / 面板 17600） ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $DATA | Out-Null
Remove-Item "$DATA\ui-settings.xml","$DATA\frps.toml","$DATA\frpc.toml" -Force -ErrorAction SilentlyContinue

$xml = @'
<?xml version="1.0" encoding="utf-8"?>
<FrpWinSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Server>
    <BindAddr>0.0.0.0</BindAddr><BindPort>17100</BindPort><KcpBindPort>0</KcpBindPort>
    <VhostHttpPort>0</VhostHttpPort><VhostHttpsPort>0</VhostHttpsPort><SubDomainHost></SubDomainHost>
    <Token>runmode-token-abc</Token><DashboardPort>17600</DashboardPort>
    <DashboardUser>admin</DashboardUser><DashboardPassword>admin</DashboardPassword>
    <LogLevel>info</LogLevel><LogMaxDays>3</LogMaxDays>
  </Server>
  <Client>
    <ServerAddr>127.0.0.1</ServerAddr><ServerPort>17100</ServerPort><Token>runmode-token-abc</Token>
    <TlsEnable>true</TlsEnable><LogLevel>info</LogLevel><LoginFailExit>false</LoginFailExit>
    <AdminPort>0</AdminPort><AdminUser>admin</AdminUser><AdminPassword>admin</AdminPassword>
    <Proxies>
      <Proxy><Name>rm-tcp</Name><Type>tcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>18081</LocalPort><RemotePort>18001</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
    </Proxies>
    <Visitors />
  </Client>
</FrpWinSettings>
'@
[System.IO.File]::WriteAllText("$DATA\ui-settings.xml", $xml, (New-Object System.Text.UTF8Encoding $false))

Write-Host "`n=== 2. 由 FrpWin.exe 生成配置 ===" -ForegroundColor Cyan
$g = Start-Process -FilePath $APP -ArgumentList '--gen-config' -Wait -PassThru
Check "gen-config 成功" ($g.ExitCode -eq 0) "exit=$($g.ExitCode)"

Write-Host "`n=== 3. FrpWin.exe --run frps 前台启动服务端 ===" -ForegroundColor Cyan
Remove-Item (Join-Path $DATA 'logs\frps.service.log') -Force -ErrorAction SilentlyContinue
$hostProc = Start-Process -FilePath $APP -ArgumentList '--run','frps' -WorkingDirectory $RUN -PassThru `
        -RedirectStandardOutput "$RUN\run.out" -RedirectStandardError "$RUN\run.err"
Start-Sleep -Seconds 6
$alive = -not $hostProc.HasExited
Check "FrpWin.exe 前台宿主进程存活" $alive "PID=$($hostProc.Id)"
if (-not $alive) { Write-Output "  退出码: $($hostProc.ExitCode)" }

Write-Host "`n=== 4. 验证 frps 真的在监听 ===" -ForegroundColor Cyan
$listening = $false
try {
    $c = New-Object System.Net.Sockets.TcpClient
    $c.Connect('127.0.0.1', 17100)
    $listening = $c.Connected
    $c.Close()
} catch { $listening = $false }
Check "控制端口 17100 已监听" $listening

$dash = $false
try {
    $c2 = New-Object System.Net.Sockets.TcpClient
    $c2.Connect('127.0.0.1', 17600)
    $dash = $c2.Connected
    $c2.Close()
} catch { $dash = $false }
Check "Dashboard 端口 17600 已监听" $dash

Write-Host "`n=== 5. 验证日志捕获与落盘 ===" -ForegroundColor Cyan
$logFile = Join-Path $DATA 'logs\frps.service.log'
Start-Sleep -Seconds 1
$logOk = Test-Path $logFile
Check "服务日志文件已生成" $logOk $logFile
if ($logOk) {
    $content = Get-Content $logFile -Raw -Encoding UTF8
    Write-Host "  日志行数: $((Get-Content $logFile).Count)"
    Get-Content $logFile -Encoding UTF8 | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" }
    Check "日志内容包含 frps 启动信息" ($content -match 'frps started successfully|listen on')
}

Write-Host "`n=== 6. 验证子进程关系 ===" -ForegroundColor Cyan
$children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($hostProc.Id)" -ErrorAction SilentlyContinue
$childNames = @($children | Select-Object -ExpandProperty Name)
Write-Output "  子进程: $($childNames -join ', ')"
Check "FrpWin.exe 拉起了 frps.exe 子进程" ($childNames -contains 'frps.exe')

Write-Host "`n=== 7. 收尾 ===" -ForegroundColor Cyan
if (-not $hostProc.HasExited) { $hostProc.Kill(); Start-Sleep -Seconds 2 }
Check "宿主进程已退出" $hostProc.HasExited
# 父进程被杀后，子进程可能残留，清理掉
Get-Process frps -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Write-Output "  清理残留 frps PID $($_.Id)" }
Start-Sleep -Milliseconds 500

Write-Host "`n=== 控制台输出 ===" -ForegroundColor Cyan
Get-Content "$RUN\run.out" -Encoding UTF8 -ErrorAction SilentlyContinue | Select-Object -First 12 | ForEach-Object { Write-Host "  $_" }

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
