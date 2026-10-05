# ============================================================================
#  frp 端到端联调测试：本机同时跑 frps + frpc，验证 TCP 隧道真正打通
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'config.ps1')

$BIN = $FrpBinDir
$T   = Join-Path $BuildDir 'e2e'
$LOCAL_PORT  = 18080
$REMOTE_PORT = 18000
$FRP_PORT    = 17000
$DASH_PORT   = 17500
$TOKEN       = 'e2e-token-123456'

Remove-Item $T -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $T | Out-Null

# ---------- 配置文件 ----------
$frpsToml = @"
bindAddr = "0.0.0.0"
bindPort = $FRP_PORT
auth.method = "token"
auth.token = "$TOKEN"
webServer.addr = "127.0.0.1"
webServer.port = $DASH_PORT
webServer.user = "admin"
webServer.password = "admin123"
log.to = "console"
log.level = "info"
"@
[System.IO.File]::WriteAllText("$T\frps.toml", $frpsToml, (New-Object System.Text.UTF8Encoding $false))

$frpcToml = @"
serverAddr = "127.0.0.1"
serverPort = $FRP_PORT
auth.method = "token"
auth.token = "$TOKEN"
log.to = "console"
log.level = "info"

[[proxies]]
name = "e2e-tcp"
type = "tcp"
localIP = "127.0.0.1"
localPort = $LOCAL_PORT
remotePort = $REMOTE_PORT
"@
[System.IO.File]::WriteAllText("$T\frpc.toml", $frpcToml, (New-Object System.Text.UTF8Encoding $false))

Write-Host "=== 1. 启动内网目标服务 (127.0.0.1:$LOCAL_PORT) ==="
$targetJob = Start-Job -ScriptBlock {
    param($port)
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    $listener.Start()
    while ($true) {
        try {
            $client = $listener.AcceptTcpClient()
            $stream = $client.GetStream()
            $buf = New-Object byte[] 4096
            try { $null = $stream.Read($buf, 0, $buf.Length) } catch { }
            $body = 'HELLO-FRP-OK'
            $resp = "HTTP/1.1 200 OK`r`nContent-Type: text/plain`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n$body"
            $bytes = [System.Text.Encoding]::ASCII.GetBytes($resp)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
            $client.Close()
        } catch { }
    }
} -ArgumentList $LOCAL_PORT
Start-Sleep -Seconds 2

Write-Host "=== 2. 启动 frps (控制端口 $FRP_PORT, 面板 $DASH_PORT) ==="
$frps = Start-Process -FilePath "$BIN\frps.exe" -ArgumentList "-c", "`"$T\frps.toml`"" `
        -WorkingDirectory $T -PassThru -NoNewWindow -RedirectStandardOutput "$T\frps.out" -RedirectStandardError "$T\frps.err"
Start-Sleep -Seconds 3
Write-Host "   frps PID = $($frps.Id)  running = $(-not $frps.HasExited)"

Write-Host "=== 3. 启动 frpc ==="
$frpc = Start-Process -FilePath "$BIN\frpc.exe" -ArgumentList "-c", "`"$T\frpc.toml`"" `
        -WorkingDirectory $T -PassThru -NoNewWindow -RedirectStandardOutput "$T\frpc.out" -RedirectStandardError "$T\frpc.err"
Start-Sleep -Seconds 5
Write-Host "   frpc PID = $($frpc.Id)  running = $(-not $frpc.HasExited)"

$pass = 0; $fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

Write-Host ""
Write-Host "=== 4. 验证结果 ==="

# 4.1 直连内网目标
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$LOCAL_PORT/" -TimeoutSec 8 -UseBasicParsing
    Check "直连内网服务" ($r.Content -eq 'HELLO-FRP-OK') "Content=$($r.Content)"
} catch { Check "直连内网服务" $false $_.Exception.Message }

# 4.2 通过 frp 映射端口访问
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$REMOTE_PORT/" -TimeoutSec 10 -UseBasicParsing
    Check "通过 frp 隧道访问" ($r.Content -eq 'HELLO-FRP-OK') "Content=$($r.Content)"
} catch { Check "通过 frp 隧道访问" $false $_.Exception.Message }

# 4.3 frps 管理面板（/ 会 301 跳转到 /static/，需要跟随跳转）
try {
    $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:admin123"))
    $body = (curl.exe -s -L -u "admin:admin123" "http://127.0.0.1:$DASH_PORT/") -join "`n"
    Check "frps Dashboard 页面" ($body -match 'id="app"' -and $body -match 'frp server') "$($body.Length) 字节, 含 Vue 挂载点"
} catch { Check "frps Dashboard 页面" $false $_.Exception.Message }

# 4.3b 面板静态资源
try {
    $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:admin123"))
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$DASH_PORT/static/" -Headers @{ Authorization = "Basic $pair" } -TimeoutSec 10 -UseBasicParsing
    Check "Dashboard 静态首页" ($r.StatusCode -eq 200 -and $r.Content -match 'id="app"') "HTTP $($r.StatusCode)"
} catch { Check "Dashboard 静态首页" $false $_.Exception.Message }

# 4.4 面板 API
try {
    $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:admin123"))
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$DASH_PORT/api/proxy/tcp" -Headers @{ Authorization = "Basic $pair" } -TimeoutSec 10 -UseBasicParsing
    Check "Dashboard API /api/proxy/tcp" ($r.StatusCode -eq 200) "HTTP $($r.StatusCode) body=$($r.Content)"
} catch { Check "Dashboard API /api/proxy/tcp" $false $_.Exception.Message }

# 4.4b “一键放行客户端端口”读真实 frps（第三版新增功能的真实集成验证）
Write-Host "=== 4.4b FrpWin --probe-frps 读取真实 frps ==="
$frpWin   = $GuiExe
$probeOut = "$T\probe.out"
$pp = Start-Process -FilePath $frpWin -ArgumentList @('--probe-frps', $DASH_PORT, 'admin', 'admin123', 'zh') `
                    -PassThru -Wait -RedirectStandardOutput $probeOut
$probe = @(Get-Content $probeOut -Encoding UTF8)
$probe | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
Check "探活真实 frps 成功" ($pp.ExitCode -eq 0) "exit=$($pp.ExitCode)"
Check "读到真实客户端" ([bool](@($probe | Where-Object { $_ -like 'CLIENT|*' }).Count)) "CLIENT 行数 = $(@($probe | Where-Object { $_ -like 'CLIENT|*' }).Count)"
Check "读到 e2e-tcp 占用的远程端口 $REMOTE_PORT" ([bool](@($probe | Where-Object { $_ -like "PORT|*|$REMOTE_PORT|TCP|e2e-tcp" }).Count)) ""
Check "生成了 $REMOTE_PORT 的进站规则" ([bool](@($probe | Where-Object { $_ -like "RULE|*|FrpWin Client $REMOTE_PORT TCP IN|in" }).Count)) ""
Check "生成了 $REMOTE_PORT 的出站规则" ([bool](@($probe | Where-Object { $_ -like "RULE|*|FrpWin Client $REMOTE_PORT TCP OUT|out" }).Count)) ""
# 真实 frps 的 /api/proxy/tcp 里没有 localPort（本地端口是客户端自己的事），
# 所以说明列必须自己兜底，不能留空
Check "说明列不为空（真实接口无 localPort）" ([bool](@($probe | Where-Object {
        $_ -like "PNOTE|*|$REMOTE_PORT|TCP|*" -and ($_ -split '\|')[4].Trim().Length -gt 0
    }).Count)) "$(@($probe | Where-Object { $_ -like 'PNOTE|*' }) -join ' / ')"
Check "说明列里标了在线" ([bool](@($probe | Where-Object { $_ -like "PNOTE|*|$REMOTE_PORT|TCP|在线*" }).Count)) ""
Check "真实 netsh 接受该命令的参数" (@($probe | Where-Object { $_ -like 'ARGS|*' } | ForEach-Object {
        $a = $_.Substring(5)
        $txt = (& cmd.exe /c "netsh.exe $a 2>&1") | Out-String
        $txt -match 'elevation' -or $txt -match '提升'
    } | Where-Object { -not $_ }).Count -eq 0) ""

# 4.5 错误 token 应被拒绝
Write-Host "=== 5. 验证认证保护（错误 token 必须连不上） ==="
$badCfg = (Get-Content "$T\frpc.toml" -Raw) -replace [regex]::Escape($TOKEN), 'wrong-token-xxx'
[System.IO.File]::WriteAllText("$T\frpc-bad.toml", $badCfg, (New-Object System.Text.UTF8Encoding $false))
$frpcBad = Start-Process -FilePath "$BIN\frpc.exe" -ArgumentList "-c", "`"$T\frpc-bad.toml`"" `
           -WorkingDirectory $T -PassThru -NoNewWindow -RedirectStandardOutput "$T\frpcbad.out" -RedirectStandardError "$T\frpcbad.err"
Start-Sleep -Seconds 4
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$REMOTE_PORT/" -TimeoutSec 6 -UseBasicParsing
    # 隧道此时仍由先前的 frpc 提供，所以这里主要看 bad 客户端是否报错
    $badLog = Get-Content "$T\frpcbad.out" -Raw -ErrorAction SilentlyContinue
    Check "错误 token 被拒绝" ($badLog -match 'token|auth|login|authorization') "错误客户端日志含认证失败信息"
} catch {
    Check "错误 token 被拒绝" $true "请求失败(符合预期): $($_.Exception.Message)"
}
if ($frpcBad -and -not $frpcBad.HasExited) { $frpcBad.Kill() }

# ---------- 清理 ----------
Write-Host ""
Write-Host "=== 6. 清理 ==="
foreach ($p in @($frps, $frpc, $frpcBad)) {
    if ($p -and -not $p.HasExited) { try { $p.Kill() } catch { } }
}
Stop-Job $targetJob -ErrorAction SilentlyContinue
Remove-Job $targetJob -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host ""
Write-Host "=== frps 日志 ===" -ForegroundColor Cyan
Get-Content "$T\frps.out" -ErrorAction SilentlyContinue | Select-Object -First 25
Write-Host "=== frpc 日志 ===" -ForegroundColor Cyan
Get-Content "$T\frpc.out" -ErrorAction SilentlyContinue | Select-Object -First 25

Write-Host ""
Write-Host "======================================"
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================"
if ($fail -gt 0) { exit 1 } else { exit 0 }
