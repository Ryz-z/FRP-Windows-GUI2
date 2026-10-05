# ============================================================================
#  “一键放行客户端端口” 后端回归测试
#
#  用 build\mock-frps.ps1 起一个假的 frps Dashboard，然后跑
#    FrpWin.exe --probe-frps <端口> <用户> <密码>
#  验证：HTTP Basic 认证、JSON 解析（含 UTF-8 与 \uXXXX）、
#        客户端归类、端口清单生成、防火墙规则名生成、各种错误提示。
# ============================================================================
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'config.ps1')

$APP      = $GuiExe
$MOCK     = Join-Path $PSScriptRoot 'mock-frps.ps1'
$TMP      = Join-Path $BuildDir 'fw'
New-Item -ItemType Directory -Force -Path $TMP | Out-Null

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

function Get-FreePort {
    $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $l.Start()
    $p = $l.LocalEndpoint.Port
    $l.Stop()
    return $p
}

$script:mockProc = $null

function Start-Mock([int]$port, [string]$mode, [string]$user = 'admin', [string]$pass2 = 'admin') {
    $log = Join-Path $TMP "mock-$mode.log"
    Remove-Item $log -Force -ErrorAction SilentlyContinue
    $script:mockProc = Start-Process -FilePath 'powershell.exe' -PassThru -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $MOCK,
        '-Port', $port, '-User', $user, '-Pass', $pass2, '-Seconds', '40', '-Mode', $mode, '-LogFile', $log
    )
    # 等端口真正开始监听
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $c = New-Object System.Net.Sockets.TcpClient
            $c.Connect('127.0.0.1', $port)
            $c.Close()
            return $true
        } catch { Start-Sleep -Milliseconds 100 }
    }
    return $false
}

function Stop-Mock {
    if ($script:mockProc) {
        try { Stop-Process -Id $script:mockProc.Id -Force -ErrorAction SilentlyContinue } catch { }
        $script:mockProc = $null
    }
    Start-Sleep -Milliseconds 200
}

function Run-Probe([int]$port, [string]$user, [string]$pass2) {
    $out = Join-Path $TMP "probe-$port.txt"
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $APP -ArgumentList @('--probe-frps', $port, $user, $pass2, 'zh') `
                       -PassThru -Wait -RedirectStandardOutput $out
    $lines = @()
    if (Test-Path $out) { $lines = @(Get-Content $out -Encoding UTF8) }
    return [pscustomobject]@{ Code = $p.ExitCode; Lines = $lines }
}

function Has($lines, $text) {
    return [bool](@($lines | Where-Object { $_ -eq $text }).Count)
}

# ============================================================================
Write-Host "`n=== 1. 正常响应：认证 + JSON 解析 + 归类 + 端口清单 ===" -ForegroundColor Cyan
$port = Get-FreePort
if (-not (Start-Mock $port 'ok')) { Write-Host "  模拟服务器起不来" -ForegroundColor Red; exit 1 }

$r = Run-Probe $port 'admin' 'admin'
$r.Lines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

Check "退出码为 0" ($r.Code -eq 0) "exit=$($r.Code)"
Check "读到服务端版本" (Has $r.Lines 'SV|0.71.0')
Check "读到一个客户端"       (Has $r.Lines 'CLIENT|home-nas|NAS-01|192.168.1.10|0.71.0|2f8a1c4e-1111-2222|online')
Check "客户端 ID 正确"       (Has $r.Lines 'PROXY|tcp|nas-ssh|2f8a1c4e-1111-2222|6000|0|online|TCP')
Check "真实接口没有 localPort 也能解析" (Has $r.Lines 'PROXY|tcp|nas-ssh|2f8a1c4e-1111-2222|6000|0|online|TCP')
Check "有 localPort 时也能解析"         (Has $r.Lines 'PROXY|tcp|nas-web|2f8a1c4e-1111-2222|8080|80|online|TCP')
Check "离线客户端也被列出"   (Has $r.Lines 'CLIENT|WIN-SERVER|WIN-SERVER|172.16.0.9|0.70.1|9c02ff13-5555-6666|offline')
Check "离线代理被标出来"     (Has $r.Lines 'PROXY|tcp|old-ghost|dead-beef-0000-0000|6099|0|offline|TCP')

Write-Host "  --- 中文（\uXXXX 转义）---" -ForegroundColor DarkGray
Check "user 里的 \uXXXX 被正确解码" (Has $r.Lines 'CLIENT|办公室电脑 测试|DESKTOP-8KQ2|10.0.0.5|0.71.0|77bd90aa-3333-4444|online')
Check "该客户端的端口也带中文名"    (Has $r.Lines 'PORT|办公室电脑 测试|13389|TCP|pc-rdp')

Write-Host "  --- 端口清单 ---" -ForegroundColor DarkGray
Check "TCP 6000 归到 home-nas"  (Has $r.Lines 'PORT|home-nas|6000|TCP|nas-ssh')
Check "TCP 8080 归到 home-nas"  (Has $r.Lines 'PORT|home-nas|8080|TCP|nas-web')
Check "TCP 8090（离线遗留）也算" (Has $r.Lines 'PORT|home-nas|8090|TCP|nas-legacy')
Check "UDP 7100 归到 home-nas"  (Has $r.Lines 'PORT|home-nas|7100|UDP|nas-game')
Check "端口数量正好 4 个"       (@($r.Lines | Where-Object { $_ -like 'PORT|home-nas|*' }).Count -eq 4)
Check "端口按号排序"            ((@($r.Lines | Where-Object { $_ -like 'PORT|home-nas|*' })[0]) -eq 'PORT|home-nas|6000|TCP|nas-ssh')
Check "http/stcp 代理写进提示"  ([bool](@($r.Lines | Where-Object { $_ -like 'NOTE|home-nas|*' -and $_ -like '*nas-blog*' -and $_ -like '*nas-p2p*' }).Count))
Check "WIN-SERVER 没有端口"     (@($r.Lines | Where-Object { $_ -like 'PORT|WIN-SERVER|*' }).Count -eq 0)
Check "WIN-SERVER 有 stcp 提示" ([bool](@($r.Lines | Where-Object { $_ -like 'NOTE|WIN-SERVER|*' }).Count))
Check "离线幽灵客户端不冒充归属" (@($r.Lines | Where-Object { $_ -like '*old-ghost*' -and $_ -like 'PORT|*' }).Count -eq 0)

Write-Host "  --- 说明列文案（真实 frps 不返回 localPort 时不能留空）---" -ForegroundColor DarkGray
Check "没有 localPort 时说明不为空" (Has $r.Lines 'PNOTE|home-nas|6000|TCP|在线 · 转发到该客户端的内网服务')
Check "有 localPort 时说明给内网地址" (Has $r.Lines 'PNOTE|home-nas|8080|TCP|在线 · 转发到内网 127.0.0.1:80')
Check "离线端口在说明里标出来"      (Has $r.Lines 'PNOTE|home-nas|8090|TCP|离线 · 转发到该客户端的内网服务')
Check "离线端口的解释写在顶部提示里" ([bool](@($r.Lines | Where-Object { $_ -like 'NOTE|home-nas|*' -and $_ -like '*离线*上一次连接*' }).Count))
Check "所有端口行都有说明"          (@($r.Lines | Where-Object { $_ -like 'PORT|*' }).Count -eq @($r.Lines | Where-Object { $_ -like 'PNOTE|*' -and ($_ -split '\|')[4].Trim().Length -gt 0 }).Count)

Write-Host "  --- 防火墙规则名 ---" -ForegroundColor DarkGray
Check "生成进站规则"   (Has $r.Lines 'RULE|home-nas|FrpWin Client 6000 TCP IN|in')
Check "生成出站规则"   (Has $r.Lines 'RULE|home-nas|FrpWin Client 6000 TCP OUT|out')
Check "UDP 规则也是 UDP" (Has $r.Lines 'RULE|home-nas|FrpWin Client 7100 UDP IN|in')
$ruleNames = @($r.Lines | Where-Object { $_ -like 'RULE|*' } | ForEach-Object { ($_ -split '\|')[2] })
Check "规则名是纯 ASCII" (@($ruleNames | Where-Object { $_ -match '[^\x20-\x7E]' }).Count -eq 0)
Check "规则名格式统一"   (@($ruleNames | Where-Object { $_ -notmatch '^FrpWin Client \d+ (TCP|UDP) (IN|OUT)$' }).Count -eq 0)
Check "http/https 不算独立端口" (@($r.Lines | Where-Object { $_ -like 'PORT|*' -and $_ -like '*nas-blog*' }).Count -eq 0)
Check "tcpmux 不算独立端口"     (@($r.Lines | Where-Object { $_ -like 'PORT|*' -and $_ -like '*tcpmux*' }).Count -eq 0)
Check "共 8 条规则（4 端口 × 2 方向）" (@($r.Lines | Where-Object { $_ -like 'RULE|home-nas|*' }).Count -eq 8)
Stop-Mock

# ============================================================================
Write-Host "`n=== 2. 密码错误：必须给出 401 的明确提示 ===" -ForegroundColor Cyan
$port2 = Get-FreePort
if (-not (Start-Mock $port2 'ok')) { Write-Host "  模拟服务器起不来" -ForegroundColor Red; exit 1 }
$r2 = Run-Probe $port2 'admin' 'wrong-password'
$r2.Lines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
Check "退出码非 0" ($r2.Code -ne 0) "exit=$($r2.Code)"
Check "提示用户名或密码不对" ([bool](@($r2.Lines | Where-Object { $_ -like 'ERR|*' -and $_ -like '*401*' -and $_ -like '*用户名或密码*' }).Count))
Stop-Mock

# ============================================================================
Write-Host "`n=== 3. 服务端没开：必须提示连接被拒绝 ===" -ForegroundColor Cyan
$port3 = Get-FreePort
$r3 = Run-Probe $port3 'admin' 'admin'
$r3.Lines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
Check "退出码非 0" ($r3.Code -ne 0) "exit=$($r3.Code)"
Check "提示连接不上并给出排查步骤" ([bool](@($r3.Lines | Where-Object { $_ -like 'ERR|*' -and $_ -like '*连接不上*' -and $_ -like '*Dashboard 端口*' }).Count))

# ============================================================================
Write-Host "`n=== 4. 返回的不是 JSON：不能崩，要给可读提示 ===" -ForegroundColor Cyan
$port4 = Get-FreePort
if (-not (Start-Mock $port4 'garbage')) { Write-Host "  模拟服务器起不来" -ForegroundColor Red; exit 1 }
$r4 = Run-Probe $port4 'admin' 'admin'
$r4.Lines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
Check "退出码非 0" ($r4.Code -ne 0) "exit=$($r4.Code)"
Check "提示不是合法 JSON" ([bool](@($r4.Lines | Where-Object { $_ -like 'ERR|*' -and $_ -like '*JSON*' }).Count))
Stop-Mock

# ============================================================================
Write-Host "`n=== 5. 老版本 frps 没有 /api/clients：要给出版本提示 ===" -ForegroundColor Cyan
$port5 = Get-FreePort
if (-not (Start-Mock $port5 'oldfrp')) { Write-Host "  模拟服务器起不来" -ForegroundColor Red; exit 1 }
$r5 = Run-Probe $port5 'admin' 'admin'
$r5.Lines | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
Check "探活仍然成功" (Has $r5.Lines 'SV|0.71.0')
Check "提示接口不存在 / 版本太老" ([bool](@($r5.Lines | Where-Object { $_ -like 'ERR|*' -and $_ -like '*404*' }).Count))
Stop-Mock

# ============================================================================
Write-Host "`n=== 6. 拿真实的 netsh 校验参数语法 ===" -ForegroundColor Cyan
Write-Host "    （没有管理员权限也没关系：netsh 会先校验参数、再检查提升）" -ForegroundColor DarkGray
$port7 = Get-FreePort
if (-not (Start-Mock $port7 'ok')) { Write-Host "  模拟服务器起不来" -ForegroundColor Red; exit 1 }
$r7 = Run-Probe $port7 'admin' 'admin'
Stop-Mock

$netshArgs = @($r7.Lines | Where-Object { $_ -like 'ARGS|*' } | ForEach-Object { $_.Substring(5) })
Check "拿到了 10 条 netsh 参数" ($netshArgs.Count -eq 10) "共 $($netshArgs.Count) 条"

$syntaxBad = @()
$unexpected = @()
foreach ($a in $netshArgs) {
    $text = (& cmd.exe /c "netsh.exe $a 2>&1") | Out-String
    if ($text -match 'not valid' -or $text -match 'Usage: add rule') { $syntaxBad += $a; continue }
    # 参数合法时，非管理员只会看到“需要提升”
    if (-not ($text -match 'elevation' -or $text -match '提升')) { $unexpected += "$a => $($text.Trim())" }
}
Check "全部 netsh 参数语法被真实 netsh 接受" ($syntaxBad.Count -eq 0) $(if ($syntaxBad.Count) { $syntaxBad[0] } else { "" })
# 注意：PowerShell 5.1 会把全角引号当成字符串定界符，参数里别用它们
Check "失败原因都只是权限不足" ($unexpected.Count -eq 0) $(if ($unexpected.Count) { $unexpected[0] } else { "" })

Write-Host "  样例命令：" -ForegroundColor DarkGray
$netshArgs | Select-Object -First 2 | ForEach-Object { Write-Host "    netsh $_" -ForegroundColor DarkGray }

# ============================================================================
Write-Host "`n=== 7. 其它参数形式 ===" -ForegroundColor Cyan
$p6 = Start-Process -FilePath $APP -ArgumentList @('--probe-frps', 'not-a-port', '', '', 'zh') -PassThru -Wait `
                    -RedirectStandardOutput (Join-Path $TMP 'bad-port.txt')
Check "端口不是数字时安全退出" ($p6.ExitCode -ne 0) "exit=$($p6.ExitCode)"

# ============================================================================
Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
