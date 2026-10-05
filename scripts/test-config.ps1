# ============================================================================
#  验证 FrpWin 图形管理器生成的 TOML 配置能被 frp 官方程序正确解析
#  覆盖全部代理类型：tcp / udp / http / https / stcp / xtcp + visitors
# ============================================================================
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'config.ps1')

$APP   = $GuiExe
$BIN   = $FrpBinDir
$DATA  = Join-Path $env:ProgramData 'FrpWin'
$FRPS  = Join-Path $DATA 'frps.toml'
$FRPC  = Join-Path $DATA 'frpc.toml'
$SET   = Join-Path $DATA 'ui-settings.xml'

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

Write-Host "=== 1. 写入一份覆盖所有代理类型的界面设置 ===" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $DATA | Out-Null
Remove-Item $SET -Force -ErrorAction SilentlyContinue

$xml = @'
<?xml version="1.0" encoding="utf-8"?>
<FrpWinSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Server>
    <BindAddr>0.0.0.0</BindAddr>
    <BindPort>17000</BindPort>
    <KcpBindPort>17001</KcpBindPort>
    <VhostHttpPort>17080</VhostHttpPort>
    <VhostHttpsPort>17443</VhostHttpsPort>
    <SubDomainHost>test.example.com</SubDomainHost>
    <Token>Tok3n-测试-!@#$%^&amp;*()</Token>
    <DashboardPort>17500</DashboardPort>
    <DashboardUser>admin</DashboardUser>
    <DashboardPassword>Passw0rd!</DashboardPassword>
    <LogLevel>debug</LogLevel>
    <LogMaxDays>7</LogMaxDays>
  </Server>
  <Client>
    <ServerAddr>127.0.0.1</ServerAddr>
    <ServerPort>17000</ServerPort>
    <Token>Tok3n-测试-!@#$%^&amp;*()</Token>
    <TlsEnable>true</TlsEnable>
    <LogLevel>debug</LogLevel>
    <LoginFailExit>false</LoginFailExit>
    <AdminPort>17400</AdminPort>
    <AdminUser>admin</AdminUser>
    <AdminPassword>Passw0rd!</AdminPassword>
    <Proxies>
      <Proxy><Name>p-tcp</Name><Type>tcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>3389</LocalPort><RemotePort>13389</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>true</UseEncryption><UseCompression>true</UseCompression></Proxy>
      <Proxy><Name>p-udp</Name><Type>udp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>5353</LocalPort><RemotePort>15353</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
      <Proxy><Name>p-http</Name><Type>http</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>80</LocalPort><RemotePort>0</RemotePort><CustomDomains>www.example.com, example.com</CustomDomains><Subdomain>web01</Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
      <Proxy><Name>p-https</Name><Type>https</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>443</LocalPort><RemotePort>0</RemotePort><CustomDomains>secure.example.com</CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
      <Proxy><Name>p-stcp</Name><Type>stcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>22</LocalPort><RemotePort>0</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey>secret-key-stcp</SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
      <Proxy><Name>p-xtcp</Name><Type>xtcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>22</LocalPort><RemotePort>0</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey>secret-key-xtcp</SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
    </Proxies>
    <Visitors>
      <Visitor><Name>v-stcp</Name><Type>stcp</Type><ServerName>p-stcp</ServerName><SecretKey>secret-key-stcp</SecretKey><BindAddr>127.0.0.1</BindAddr><BindPort>19000</BindPort></Visitor>
      <Visitor><Name>v-xtcp</Name><Type>xtcp</Type><ServerName>p-xtcp</ServerName><SecretKey>secret-key-xtcp</SecretKey><BindAddr>127.0.0.1</BindAddr><BindPort>19001</BindPort></Visitor>
    </Visitors>
  </Client>
</FrpWinSettings>
'@
[System.IO.File]::WriteAllText($SET, $xml, (New-Object System.Text.UTF8Encoding $false))
Check "界面设置文件已写入" (Test-Path $SET)

Write-Host "`n=== 2. 调用 FrpWin.exe --gen-config 生成 TOML ===" -ForegroundColor Cyan
Remove-Item $FRPS, $FRPC -Force -ErrorAction SilentlyContinue
$g = Start-Process -FilePath $APP -ArgumentList '--gen-config' -Wait -PassThru
Check "gen-config 退出码为 0" ($g.ExitCode -eq 0) "exit=$($g.ExitCode)"
Check "已生成 frps.toml" (Test-Path $FRPS)
Check "已生成 frpc.toml" (Test-Path $FRPC)

Write-Host "`n=== 3. 生成的 frps.toml ===" -ForegroundColor Cyan
Get-Content $FRPS | ForEach-Object { Write-Host "  $_" }

Write-Host "`n=== 4. 生成的 frpc.toml ===" -ForegroundColor Cyan
Get-Content $FRPC | ForEach-Object { Write-Host "  $_" }

Write-Host "`n=== 5. 用 frp 官方 verify 命令校验 ===" -ForegroundColor Cyan
$v1 = & "$BIN\frps.exe" verify -c "$FRPS" 2>&1
$c1 = $LASTEXITCODE
Write-Host "  frps verify 输出: $($v1 -join ' | ')"
Check "frps 接受生成的配置" ($c1 -eq 0) "exit=$c1"

$v2 = & "$BIN\frpc.exe" verify -c "$FRPC" 2>&1
$c2 = $LASTEXITCODE
Write-Host "  frpc verify 输出: $($v2 -join ' | ')"
Check "frpc 接受生成的配置" ($c2 -eq 0) "exit=$c2"

Write-Host "`n=== 6. 校验关键内容 ===" -ForegroundColor Cyan
$frpsTxt = Get-Content $FRPS -Raw
$frpcTxt = Get-Content $FRPC -Raw
Check "frps 含 bindPort" ($frpsTxt -match 'bindPort = 17000')
Check "frps 含 kcpBindPort" ($frpsTxt -match 'kcpBindPort = 17001')
Check "frps 含 vhostHTTPPort" ($frpsTxt -match 'vhostHTTPPort = 17080')
Check "frps 含 vhostHTTPSPort" ($frpsTxt -match 'vhostHTTPSPort = 17443')
Check "frps 含 subDomainHost" ($frpsTxt -match 'subDomainHost = "test\.example\.com"')
Check "frps 含 Dashboard 配置" ($frpsTxt -match 'webServer\.port = 17500')
Check "token 特殊字符被正确转义" ($frpcTxt -match 'auth\.token = "Tok3n')
Check "frpc 含 6 条代理" (([regex]::Matches($frpcTxt, '\[\[proxies\]\]')).Count -eq 6) "实际 $(([regex]::Matches($frpcTxt,'\[\[proxies\]\]')).Count) 条"
Check "frpc 含 2 条访问者" (([regex]::Matches($frpcTxt, '\[\[visitors\]\]')).Count -eq 2) "实际 $(([regex]::Matches($frpcTxt,'\[\[visitors\]\]')).Count) 条"
Check "http 代理含 customDomains" ($frpcTxt -match 'customDomains = \["www\.example\.com", "example\.com"\]')
Check "stcp 代理含 secretKey" ($frpcTxt -match 'secretKey = "secret-key-stcp"')
Check "tcp 代理含加密压缩开关" ($frpcTxt -match 'transport\.useEncryption = true' -and $frpcTxt -match 'transport\.useCompression = true')
Check "visitor 含 serverName/bindPort" ($frpcTxt -match 'serverName = "p-stcp"' -and $frpcTxt -match 'bindPort = 19000')

Write-Host "`n=== 7. 真实启动一次，确认服务端能跑起来 ===" -ForegroundColor Cyan
$f = Start-Process -FilePath "$BIN\frps.exe" -ArgumentList "-c", "`"$FRPS`"" -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\vv.out" -RedirectStandardError "$env:TEMP\vv.err"
Start-Sleep -Seconds 3
$alive = -not $f.HasExited
Check "服务端使用 GUI 生成的配置成功启动" $alive "PID=$($f.Id)"
if (-not $alive) { Get-Content "$env:TEMP\vv.out","$env:TEMP\vv.err" -ErrorAction SilentlyContinue | Select-Object -First 10 }
if ($alive) { $f.Kill() }
Start-Sleep -Milliseconds 500

Write-Host "`n=== 8. 多服务端连接：一个客户端配两个服务端 ===" -ForegroundColor Cyan
# 第四版新增：连接设置里可以「新添服务器」，每个连接生成一份 frpc-N.toml
$FRPC2 = Join-Path $DATA 'frpc-2.toml'
$FRPC3 = Join-Path $DATA 'frpc-3.toml'
Remove-Item $FRPC, $FRPC2, $FRPC3 -Force -ErrorAction SilentlyContinue

$xml2 = @'
<?xml version="1.0" encoding="utf-8"?>
<FrpWinSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Language>zh</Language>
  <Server>
    <BindAddr>0.0.0.0</BindAddr>
    <BindPort>17000</BindPort>
    <KcpBindPort>0</KcpBindPort>
    <VhostHttpPort>0</VhostHttpPort>
    <VhostHttpsPort>0</VhostHttpsPort>
    <SubDomainHost></SubDomainHost>
    <Token>multi-token</Token>
    <DashboardPort>17500</DashboardPort>
    <DashboardUser>admin</DashboardUser>
    <DashboardPassword>admin</DashboardPassword>
    <LogLevel>info</LogLevel>
    <LogMaxDays>3</LogMaxDays>
  </Server>
  <Client>
    <Selected>1</Selected>
    <Connections>
      <Connection>
        <Id>conn-aaa</Id><Label>公司服务器</Label><Enabled>true</Enabled>
        <ServerAddr>10.1.1.1</ServerAddr><ServerPort>7000</ServerPort>
        <Token>tok-A</Token><TlsEnable>true</TlsEnable><LogLevel>info</LogLevel>
        <LoginFailExit>false</LoginFailExit><AdminPort>0</AdminPort>
        <AdminUser>admin</AdminUser><AdminPassword>admin</AdminPassword>
        <Proxies>
          <Proxy><Name>a-rdp</Name><Type>tcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>3389</LocalPort><RemotePort>13389</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
        </Proxies>
        <Visitors />
      </Connection>
      <Connection>
        <Id>conn-bbb</Id><Label>家里 NAS</Label><Enabled>false</Enabled>
        <ServerAddr>nas.example.com</ServerAddr><ServerPort>7001</ServerPort>
        <Token>tok-B</Token><TlsEnable>false</TlsEnable><LogLevel>debug</LogLevel>
        <LoginFailExit>true</LoginFailExit><AdminPort>17401</AdminPort>
        <AdminUser>nas</AdminUser><AdminPassword>naspass</AdminPassword>
        <Proxies>
          <Proxy><Name>b-web</Name><Type>tcp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>80</LocalPort><RemotePort>18080</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
          <Proxy><Name>b-udp</Name><Type>udp</Type><LocalIP>127.0.0.1</LocalIP><LocalPort>5353</LocalPort><RemotePort>15353</RemotePort><CustomDomains></CustomDomains><Subdomain></Subdomain><SecretKey></SecretKey><UseEncryption>false</UseEncryption><UseCompression>false</UseCompression></Proxy>
        </Proxies>
        <Visitors />
      </Connection>
    </Connections>
  </Client>
</FrpWinSettings>
'@
[System.IO.File]::WriteAllText($SET, $xml2, (New-Object System.Text.UTF8Encoding $false))

$g2 = Start-Process -FilePath $APP -ArgumentList '--gen-config' -Wait -PassThru
Check "gen-config 退出码为 0" ($g2.ExitCode -eq 0) "exit=$($g2.ExitCode)"
Check "第 1 个连接生成 frpc.toml" (Test-Path $FRPC)
Check "第 2 个连接生成 frpc-2.toml" (Test-Path $FRPC2)
Check "不会留下多余的 frpc-3.toml" (-not (Test-Path $FRPC3))

$c1txt = Get-Content $FRPC -Raw -Encoding UTF8
$c2txt = Get-Content $FRPC2 -Raw -Encoding UTF8
Check "第 1 个连接指向 10.1.1.1:7000" ($c1txt -match 'serverAddr = "10\.1\.1\.1"' -and $c1txt -match 'serverPort = 7000')
Check "第 1 个连接用自己的 token" ($c1txt -match 'auth\.token = "tok-A"')
Check "第 1 个连接只有 1 条代理" (([regex]::Matches($c1txt, '\[\[proxies\]\]')).Count -eq 1)
Check "第 2 个连接指向 nas.example.com:7001" ($c2txt -match 'serverAddr = "nas\.example\.com"' -and $c2txt -match 'serverPort = 7001')
Check "第 2 个连接用自己的 token" ($c2txt -match 'auth\.token = "tok-B"')
Check "第 2 个连接有 2 条代理" (([regex]::Matches($c2txt, '\[\[proxies\]\]')).Count -eq 2)
Check "第 2 个连接的 TLS 是关的" ($c2txt -match 'transport\.tls\.enable = false')
Check "第 2 个连接有本地管理界面" ($c2txt -match 'webServer\.port = 17401')
Check "两个连接的文件头各自带连接名" ($c1txt -match '公司服务器' -and $c2txt -match '家里 NAS')

foreach ($f in @($FRPC, $FRPC2)) {
    $vv = & "$BIN\frpc.exe" verify -c "$f" 2>&1
    Check "frpc verify 通过 $(Split-Path $f -Leaf)" ($LASTEXITCODE -eq 0) ($vv -join ' | ')
}

Write-Host "`n=== 9. 旧版单连接设置能自动迁移 ===" -ForegroundColor Cyan
[System.IO.File]::WriteAllText($SET, $xml, (New-Object System.Text.UTF8Encoding $false))
$g3 = Start-Process -FilePath $APP -ArgumentList '--gen-config' -Wait -PassThru
Check "迁移后 gen-config 退出码为 0" ($g3.ExitCode -eq 0) "exit=$($g3.ExitCode)"
$mig = Get-Content $SET -Raw -Encoding UTF8
Check "老设置被迁移成一个连接" ($mig -match '<Connections>' -and ([regex]::Matches($mig, '<Connection>')).Count -eq 1)
Check "迁移后 6 条代理一条不少" (([regex]::Matches((Get-Content $FRPC -Raw -Encoding UTF8), '\[\[proxies\]\]')).Count -eq 6) "实际 $(([regex]::Matches((Get-Content $FRPC -Raw),'\[\[proxies\]\]')).Count) 条"
Check "迁移后 2 条访问者一条不少" (([regex]::Matches((Get-Content $FRPC -Raw -Encoding UTF8), '\[\[visitors\]\]')).Count -eq 2)
Check "迁移时顺手删掉了多余的 frpc-2.toml" (-not (Test-Path $FRPC2))

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
