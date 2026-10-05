param(
    [int]$Port = 17500,
    [string]$User = 'admin',
    [string]$Pass = 'admin',
    [int]$Seconds = 90,
    [string]$Mode = 'ok',        # ok | unauthorized | garbage | oldfrp
    [string]$LogFile = ''
)

# ============================================================================
#  极简 frps Dashboard 模拟服务器
#  用 TcpListener 手写 HTTP，不依赖 HttpListener 的 URL ACL，普通用户也能跑。
#  只为验证 FrpWin 的 JSON 解析、客户端归类和端口清单生成，不实现真实 frp 逻辑。
# ============================================================================
$ErrorActionPreference = 'Continue'

$expectedAuth = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$User`:$Pass"))

function Write-Log([string]$text) {
    if ($LogFile) {
        try { [System.IO.File]::AppendAllText($LogFile, $text + "`r`n", (New-Object System.Text.UTF8Encoding $false)) } catch { }
    }
}

$serverInfo = @'
{"version":"0.71.0","bindPort":7000,"vhostHTTPPort":80,"vhostHTTPSPort":443,"kcpBindPort":0,"subdomainHost":"","maxPoolCount":5,"maxPortsPerClient":0,"heartbeatTimeout":90,"totalTrafficIn":123,"totalTrafficOut":456,"curConns":3,"clientCounts":3,"proxyTypeCount":{"tcp":4,"udp":2}}
'@

# 故意混入中文和转义字符，验证 UTF-8 与 \uXXXX 解析
$clients = @'
[
 {"key":"k1","user":"home-nas","clientID":"2f8a1c4e-1111-2222","runID":"r1","version":"0.71.0","wireProtocol":"v2","hostname":"NAS-01","clientIP":"192.168.1.10","firstConnectedAt":1700000000,"lastConnectedAt":1700000100,"online":true},
 {"key":"k2","user":"办公室电脑 \u6d4b\u8bd5","clientID":"77bd90aa-3333-4444","runID":"r2","version":"0.71.0","hostname":"DESKTOP-8KQ2","clientIP":"10.0.0.5","firstConnectedAt":1700000000,"lastConnectedAt":1700000100,"online":true},
 {"key":"k3","user":"","clientID":"9c02ff13-5555-6666","runID":"r3","version":"0.70.1","hostname":"WIN-SERVER","clientIP":"172.16.0.9","firstConnectedAt":1700000000,"lastConnectedAt":1700000100,"online":false}
]
'@

$proxyTcp = @'
{"proxies":[
 {"name":"nas-ssh","conf":{"name":"nas-ssh","type":"tcp","localIP":"127.0.0.1","remotePort":6000,"transport":{"useEncryption":false,"useCompression":false}},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","todayTrafficIn":1,"todayTrafficOut":2,"curConns":1,"lastStartTime":"","lastCloseTime":"","status":"online"},
 {"name":"nas-web","conf":{"name":"nas-web","type":"tcp","localIP":"127.0.0.1","localPort":80,"remotePort":8080},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","todayTrafficIn":0,"todayTrafficOut":0,"curConns":0,"lastStartTime":"","lastCloseTime":"","status":"online"},
 {"name":"nas-legacy","conf":{"name":"nas-legacy","type":"tcp","localIP":"127.0.0.1","remotePort":8090},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","todayTrafficIn":0,"todayTrafficOut":0,"curConns":0,"lastStartTime":"","lastCloseTime":"","status":"offline"},
 {"name":"pc-rdp","conf":{"name":"pc-rdp","type":"tcp","localIP":"127.0.0.1","remotePort":13389},"user":"办公室电脑 \u6d4b\u8bd5","clientID":"77bd90aa-3333-4444","todayTrafficIn":0,"todayTrafficOut":0,"curConns":0,"lastStartTime":"","lastCloseTime":"","status":"online"},
 {"name":"old-ghost","conf":{"name":"old-ghost","type":"tcp","localIP":"127.0.0.1","remotePort":6099},"user":"nobody","clientID":"dead-beef-0000-0000","todayTrafficIn":0,"todayTrafficOut":0,"curConns":0,"lastStartTime":"","lastCloseTime":"","status":"offline"}
]}
'@

# 真实的 frp v0.71 里 /api/proxy/tcp 的 conf 只有 localIP 的默认值、没有 localPort
# （本地端口是客户端自己的事，frps 不记录），所以上面大部分代理故意不带 localPort，
# 只留 nas-web 一个带 localPort 的样本，两个分支都能覆盖到。
$proxyUdp = @'
{"proxies":[
 {"name":"nas-game","conf":{"name":"nas-game","type":"udp","localIP":"127.0.0.1","remotePort":7100},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","status":"online"}
]}
'@

$proxyHttp = @'
{"proxies":[
 {"name":"nas-blog","conf":{"name":"nas-blog","type":"http","localIP":"127.0.0.1","localPort":80,"customDomains":["blog.example.com"],"subdomain":""},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","status":"online"}
]}
'@

$proxyStcp = @'
{"proxies":[
 {"name":"nas-p2p","conf":{"name":"nas-p2p","type":"stcp","localIP":"127.0.0.1","localPort":3306},"user":"home-nas","clientID":"2f8a1c4e-1111-2222","status":"online"},
 {"name":"srv-db","conf":{"name":"srv-db","type":"stcp","localIP":"127.0.0.1","localPort":5432},"user":"","clientID":"9c02ff13-5555-6666","status":"online"}
]}
'@

$empty = '{"proxies":[]}'

function Get-Body([string]$path) {
    $clean = ($path -split '\?')[0].TrimEnd('/')
    if ($clean -eq '/api/serverinfo') { return $serverInfo }
    if ($clean -eq '/api/clients') { return $clients }

    # 必须精确匹配最后一段：用 -like '/api/proxy/tcp*' 会把 tcpmux 也匹配上，
    # 用 '/api/proxy/http*' 会把 https 也匹配上。
    if ($clean -match '^/api/proxy/([A-Za-z0-9]+)$') {
        switch ($Matches[1]) {
            'tcp'  { return $proxyTcp }
            'udp'  { return $proxyUdp }
            'http' { return $proxyHttp }
            'stcp' { return $proxyStcp }
            default { return $empty }
        }
    }
    return $empty
}

$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Log "mock frps 已启动 127.0.0.1:$Port mode=$Mode"

$deadline = (Get-Date).AddSeconds($Seconds)
$served = 0

while ((Get-Date) -lt $deadline) {
    if (-not $listener.Pending()) { Start-Sleep -Milliseconds 30; continue }

    $client = $null
    try {
        $client = $listener.AcceptTcpClient()
        $client.ReceiveTimeout = 3000
        $stream = $client.GetStream()

        # ---- 读请求头 ----
        $sb = New-Object System.Text.StringBuilder
        $buf = New-Object byte[] 4096
        do {
            $n = $stream.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            [void]$sb.Append([Text.Encoding]::ASCII.GetString($buf, 0, $n))
        } while ($sb.ToString() -notmatch "`r`n`r`n")

        $head = $sb.ToString().Split("`r`n")
        $requestLine = $head[0]
        $authLine = ($head | Where-Object { $_ -match '^Authorization:' } | Select-Object -First 1)
        $auth = if ($authLine) { $authLine.Substring(14).Trim() } else { '' }

        $path = '/'
        if ($requestLine -match '^\S+\s+(\S+)') { $path = $Matches[1] }

        $status = 200; $statusText = 'OK'; $body = ''

        if ($Mode -eq 'unauthorized') {
            $status = 401; $statusText = 'Unauthorized'; $body = '{"code":401,"msg":"invalid user or password"}'
        }
        elseif ($Mode -eq 'garbage') {
            $body = 'this is not json at all'
        }
        elseif ($Mode -eq 'oldfrp') {
            if ($path -like '/api/clients*') { $status = 404; $statusText = 'Not Found'; $body = '404 page not found' }
            elseif ($path -like '/api/serverinfo*') { $body = $serverInfo }
            else { $body = $empty }
        }
        elseif ($auth -ne $expectedAuth) {
            $status = 401; $statusText = 'Unauthorized'; $body = '{"code":401,"msg":"invalid user or password"}'
        }
        else {
            $body = Get-Body $path
        }

        $bodyBytes = [Text.Encoding]::UTF8.GetBytes($body)
        $header = "HTTP/1.1 $status $statusText`r`n" +
                  "Content-Type: application/json; charset=utf-8`r`n" +
                  "Content-Length: $($bodyBytes.Length)`r`n" +
                  "Connection: close`r`n`r`n"
        $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
        $stream.Write($headerBytes, 0, $headerBytes.Length)
        $stream.Write($bodyBytes, 0, $bodyBytes.Length)
        $stream.Flush()

        $served++
        Write-Log "$status $path"
    }
    catch {
        Write-Log "处理请求出错: $($_.Exception.Message)"
    }
    finally {
        if ($client) { try { $client.Close() } catch { } }
    }
}

$listener.Stop()
Write-Log "mock frps 退出，共处理 $served 个请求"
