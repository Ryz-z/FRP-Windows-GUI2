# ============================================================================
#  「导入 frpc 配置」回归测试
#
#  验证 FrpWin.exe --import-frpc <文件> 能正确认出别人机器上的 frpc 配置：
#    · 新版 TOML 格式（[[proxies]] / [[visitors]] / 点号键）
#    · 老版 INI 格式（[common] + [ssh] 这种段落，靠 type 键定性）
#    · includes 递归展开
#    · 界面外的参数（healthCheck 等）原样保留，不丢
#  这一套跑的是和界面上「导入配置」按钮完全相同的解析代码。
# ============================================================================
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'config.ps1')
$APP   = $GuiExe
$IMPDIR   = Join-Path $BuildDir 'import'
New-Item -ItemType Directory -Force -Path $IMPDIR | Out-Null

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

if (-not (Test-Path $APP)) { Write-Host "找不到 $APP，请先运行 build.ps1 -SkipInstaller" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
#  造三份「别人的」配置
# ---------------------------------------------------------------------------
$toml = Join-Path $IMPDIR 'other-frpc.toml'
[System.IO.File]::WriteAllText($toml, @'
# 别人机器上的 frpc 配置，拷贝过来验证导入识别
serverAddr = "203.0.113.10"      # 注释里的井号不该被当成值
serverPort = 7000
loginFailExit = false

auth.method = "token"
auth.token = "Imported_Token_123"

transport.tls.enable = true
transport.tls.serverName = "example.com"

log.to = "console"
log.level = "warn"

webServer.addr = "127.0.0.1"
webServer.port = 7400
webServer.user = "impuser"
webServer.password = "imppass"

[[proxies]]
name = "rdp"
type = "tcp"
localIP = "127.0.0.1"
localPort = 3389
remotePort = 13389
transport.useEncryption = true

[[proxies]]
name = "nas-http"
type = "http"
localPort = 5000
customDomains = ["nas.example.com", "nas2.example.com"]
transport.useCompression = true
healthCheck.type = "http"
healthCheck.path = "/healthz"
healthCheck.intervalSeconds = 10

[[proxies]]
name = "p2p-db"
type = "stcp"
localIP = "10.0.0.5"
localPort = 3306
secretKey = "p2p-Secret-Key"

[[visitors]]
name = "visitor-db"
type = "stcp"
serverName = "p2p-db"
secretKey = "p2p-Secret-Key"
bindAddr = "127.0.0.1"
bindPort = 13306
'@, (New-Object System.Text.UTF8Encoding $false))

$ini = Join-Path $IMPDIR 'other-frpc.ini'
[System.IO.File]::WriteAllText($ini, @'
# 老版本 frpc.ini 格式（frp 0.52 之前）
[common]
server_addr = 198.51.100.7
server_port = 7000
token = Legacy_Ini_Token
tls_enable = true
log_level = info

[web]
type = http
local_port = 8080
custom_domains = old.example.com

[ssh]
type = tcp
local_ip = 127.0.0.1
local_port = 22
remote_port = 16022
use_encryption = true
use_compression = true
'@, (New-Object System.Text.UTF8Encoding $false))

# includes：主文件不带代理，代理和访问者都在被包含的文件里
$incPart = Join-Path $IMPDIR 'inc-proxies.toml'
[System.IO.File]::WriteAllText($incPart, @'
[[proxies]]
name = "inc-web"
type = "https"
localPort = 8443
customDomains = ["inc.example.com"]

[[visitors]]
name = "inc-visitor"
type = "xtcp"
serverName = "p2p-db"
secretKey = "inc-secret"
bindPort = 13307
'@, (New-Object System.Text.UTF8Encoding $false))

$incMain = Join-Path $IMPDIR 'other-with-includes.toml'
[System.IO.File]::WriteAllText($incMain, @'
serverAddr = "192.0.2.77"
serverPort = 7000
auth.token = "Include_Token"

includes = ["inc-proxies.toml"]
'@, (New-Object System.Text.UTF8Encoding $false))

# 不带服务端地址的配置（只有代理规则，服务端信息在别处）
$only = Join-Path $IMPDIR 'other-proxies-only.toml'
[System.IO.File]::WriteAllText($only, @'
[[proxies]]
name = "only-one"
type = "tcp"
localPort = 1234
remotePort = 15678
'@, (New-Object System.Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------
function Import($file) {
    $out = Join-Path $IMPDIR ('dump-' + [System.IO.Path]::GetFileName($file) + '.txt')
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $APP -ArgumentList '--import-frpc', $file -PassThru -Wait -RedirectStandardOutput $out
    return [pscustomobject]@{ Code = $p.ExitCode; Lines = @(Get-Content $out -Encoding UTF8) }
}

function LinesOf($res, $prefix) { return @($res.Lines | Where-Object { $_ -like "$prefix*" }) }
function Field($line, $index) { return ($line -split '\|')[$index] }

# ===========================================================================
Write-Host "`n=== 1. 新版 TOML 格式 ===" -ForegroundColor Cyan
$r = Import $toml
Check "解析退出码为 0" ($r.Code -eq 0) "exit=$($r.Code) 行数=$($r.Lines.Count)"

$conn = LinesOf $r 'CONN|' | Select-Object -First 1
Check "读到 CONN 行" ([bool]$conn) "$conn"
Check "认出服务器地址" ((Field $conn 1) -eq '203.0.113.10') "$(Field $conn 1)"
Check "认出服务器端口" ((Field $conn 2) -eq '7000') "$(Field $conn 2)"
Check "标记有服务端信息" ((Field $conn 3) -eq '1') "$(Field $conn 3)"
Check "认出 token" ((Field $conn 4) -eq 'Imported_Token_123') "$(Field $conn 4)"
Check "认出 TLS 打开" ((Field $conn 6) -eq 'true' -and (Field $conn 7) -eq '1') "$(Field $conn 6)/$(Field $conn 7)"
Check "认出日志级别 warn" ((Field $conn 8) -eq 'warn') "$(Field $conn 8)"
Check "认出本地管理界面端口" ((Field $conn 10) -eq '7400') "$(Field $conn 10)"

$proxies = LinesOf $r 'PROXY|'
Check "读出 3 条代理规则" ($proxies.Count -eq 3) "共 $($proxies.Count) 条"
$p1 = $proxies | Where-Object { (Field $_ 1) -eq 'rdp' } | Select-Object -First 1
Check "tcp 规则字段完整（本地/远程端口）" ((Field $p1 4) -eq '3389' -and (Field $p1 5) -eq '13389') "$p1"
Check "tcp 规则的加密开关" ((Field $p1 9) -eq 'true') "$(Field $p1 9)"
$p2 = $proxies | Where-Object { (Field $_ 1) -eq 'nas-http' } | Select-Object -First 1
Check "http 规则认出多个自定义域名" ((Field $p2 6) -eq 'nas.example.com, nas2.example.com') "$(Field $p2 6)"
Check "http 规则的压缩开关" ((Field $p2 10) -eq 'true') "$(Field $p2 10)"
$p3 = $proxies | Where-Object { (Field $_ 1) -eq 'p2p-db' } | Select-Object -First 1
Check "stcp 规则认出密钥" ((Field $p3 8) -eq 'p2p-Secret-Key') "$(Field $p3 8)"

$pextra = LinesOf $r 'PEXTRA|'
Check "healthCheck 三个键被原样保留" ($pextra.Count -eq 3) "共 $($pextra.Count) 条"
Check "保留的键内容正确" ([bool]($pextra | Where-Object { $_ -like '*healthCheck.intervalSeconds = 10' })) ($pextra -join ' / ')

$vis = @(LinesOf $r 'VISITOR|')
Check "读出 1 条访问者" ($vis.Count -eq 1) "共 $($vis.Count) 条"
Check "访问者字段完整" ((Field $vis[0] 1) -eq 'visitor-db' -and (Field $vis[0] 3) -eq 'p2p-db' -and (Field $vis[0] 6) -eq '13306') "$($vis[0])"

# ===========================================================================
Write-Host "`n=== 2. 老版 INI 格式 ===" -ForegroundColor Cyan
$r2 = Import $ini
Check "解析退出码为 0" ($r2.Code -eq 0) "exit=$($r2.Code)"
$conn2 = LinesOf $r2 'CONN|' | Select-Object -First 1
Check "INI 认出 server_addr / token" ((Field $conn2 1) -eq '198.51.100.7' -and (Field $conn2 4) -eq 'Legacy_Ini_Token') "$conn2"
$proxies2 = @(LinesOf $r2 'PROXY|')
Check "INI 读出 2 条代理规则" ($proxies2.Count -eq 2) "共 $($proxies2.Count) 条（[web] 和 [ssh]；没有 type 的段落不算）"
$web = $proxies2 | Where-Object { (Field $_ 2) -eq 'http' } | Select-Object -First 1
Check "INI 的 [web] 段落被认成 http 规则" ([bool]$web) "$web"
Check "INI 的自定义域名正确" ((Field $web 6) -eq 'old.example.com') "$(Field $web 6)"
$ssh = $proxies2 | Where-Object { (Field $_ 2) -eq 'tcp' } | Select-Object -First 1
Check "INI 的 [ssh] 段落被认成 tcp 规则" ([bool]$ssh) "$ssh"
Check "INI 的远程端口正确" ((Field $ssh 5) -eq '16022') "$(Field $ssh 5)"
Check "INI 段落名自动补成不重名的名字" ((Field $ssh 1) -match '^auto-') "$(Field $ssh 1)"

# ===========================================================================
Write-Host "`n=== 3. includes 递归展开 ===" -ForegroundColor Cyan
$r3 = Import $incMain
Check "解析退出码为 0" ($r3.Code -eq 0) "exit=$($r3.Code)"
$files3 = LinesOf $r3 'FILE|'
Check "读到 2 个文件（主文件 + 被 include 的）" ($files3.Count -eq 2) "共 $($files3.Count) 个"
Check "被 include 的文件确实读了" ([bool]($files3 | Where-Object { $_ -like '*inc-proxies.toml' })) ($files3 -join ' / ')
$proxies3 = LinesOf $r3 'PROXY|'
Check "include 里的代理规则也被导入" ([bool]($proxies3 | Where-Object { (Field $_ 1) -eq 'inc-web' })) "$($proxies3 -join ' / ')"
$vis3 = LinesOf $r3 'VISITOR|'
Check "include 里的访问者也被导入" ([bool]($vis3 | Where-Object { (Field $_ 1) -eq 'inc-visitor' })) "$($vis3 -join ' / ')"

# ===========================================================================
Write-Host "`n=== 4. 没有服务端地址的配置 ===" -ForegroundColor Cyan
$r4 = Import $only
Check "解析退出码为 0" ($r4.Code -eq 0) "exit=$($r4.Code)"
$conn4 = LinesOf $r4 'CONN|' | Select-Object -First 1
Check "标记为没有服务端信息" ((Field $conn4 3) -eq '0') "$conn4"
Check "仍然导出了代理规则" ((LinesOf $r4 'PROXY|').Count -eq 1) "$((LinesOf $r4 'PROXY|') -join ' / ')"

# ===========================================================================
Write-Host "`n=== 5. 坏输入不能崩 ===" -ForegroundColor Cyan
$bad = Join-Path $IMPDIR 'bad.toml'
[System.IO.File]::WriteAllText($bad, "这不是一个配置[[[ = = =" + "`n" + "serverAddr = " + "`n", (New-Object System.Text.UTF8Encoding $false))
$r5 = Import $bad
Check "半截配置也能正常返回（不抛异常）" ($r5.Code -eq 0 -or $r5.Code -eq 2) "exit=$($r5.Code)"
Check "返回内容里没有未捕获异常" (-not ($r5.Lines | Where-Object { $_ -like '*NullReference*' -or $_ -like '*未将对象引用*' })) ($r5.Lines -join ' / ')

$missing = Import (Join-Path $IMPDIR 'does-not-exist.toml')
Check "文件不存在时不崩" ($missing.Code -eq 0 -or $missing.Code -eq 2) "exit=$($missing.Code)"
Check "文件不存在时没有代理规则" ((LinesOf $missing 'PROXY|').Count -eq 0)

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
