# ============================================================================
#  下载 frp 官方源码并编译出 frps.exe / frpc.exe（含 Web 管理面板前端）
#
#  用法：
#    .\scripts\fetch-frp.ps1              下载并编译
#    .\scripts\fetch-frp.ps1 -Force       已存在也重新下载编译
#    .\scripts\fetch-frp.ps1 -SkipWeb     跳过前端构建（frps 将没有 Dashboard 页面）
#
#  需要的环境：Go 1.25+、Node.js 18+（-SkipWeb 时不需要 Node）
# ============================================================================
param(
    [switch]$Force,
    [switch]$SkipWeb,
    [string]$GoProxy = 'https://goproxy.cn,direct',
    [string]$SourceUrl = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'config.ps1')

Write-Step "准备第三方目录 $ThirdPartyDir"
New-Item -ItemType Directory -Force -Path $ThirdPartyDir | Out-Null

# ---------------------------------------------------------------- 1. 下载源码
$zipPath = Join-Path $ThirdPartyDir "frp-$FrpVersion.zip"

if ($Force -or -not (Test-Path $FrpSrcDir)) {
    if ($Force -or -not (Test-Path $zipPath)) {
        Write-Host "  下载 frp v$FrpVersion 源码 ..."

        # 按顺序试多个地址：
        #   1) codeload 直链，不经过 github.com 跳转，国内网络通常更稳
        #   2) github.com 官方归档地址
        #   3) GitHub 加速代理
        # 都不通可以用 -SourceUrl 指定自己的镜像。
        $urls = @()
        if ($SourceUrl) { $urls += $SourceUrl }
        $urls += @(
            "https://codeload.github.com/fatedier/frp/zip/refs/tags/v$FrpVersion",
            "https://github.com/fatedier/frp/archive/refs/tags/v$FrpVersion.zip",
            "https://ghproxy.net/https://github.com/fatedier/frp/archive/refs/tags/v$FrpVersion.zip"
        )

        $ok = $false
        foreach ($url in $urls) {
            Write-Host "    尝试 $url"
            Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
            $code = Invoke-Native -File 'curl.exe' -Arguments @(
                '-L','--fail','--silent','--show-error','--connect-timeout','20','--max-time','600',
                '--retry','2','--retry-all-errors','-o',$zipPath,$url) -Quiet
            if ($code -eq 0 -and (Test-Path $zipPath) -and (Get-Item $zipPath).Length -gt 100KB) {
                Write-Host ("    成功（{0:N0} KB）" -f ((Get-Item $zipPath).Length / 1KB))
                $ok = $true
                break
            }
            Write-Host "    失败（退出码 $code）"
        }
        if (-not $ok) {
            throw "所有下载地址都失败了。可以手工下载 frp v$FrpVersion 源码包放到 $zipPath，或加 -SourceUrl <你自己的镜像地址>"
        }
    }
    Write-Host "  解压 ..."
    if (Test-Path $FrpSrcDir) { Remove-Item $FrpSrcDir -Recurse -Force }
    $tmp = Join-Path $ThirdPartyDir '_unzip'
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force
    if (-not (Test-Path (Join-Path $tmp "frp-$FrpVersion"))) {
        throw "解压后的目录结构不是预期的 frp-$FrpVersion，请检查压缩包"
    }
    Move-Item (Join-Path $tmp "frp-$FrpVersion") $FrpSrcDir
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  源码就绪：$FrpSrcDir"
} else {
    Write-Host "  源码已存在，跳过下载（要重新下载请加 -Force）"
}

# ------------------------------------------------------------ 2. 构建 Web 面板
if (-not $SkipWeb) {
    $webDir = Join-Path $FrpSrcDir 'web'
    $frpsDist = Join-Path $webDir 'frps\dist'
    $frpcDist = Join-Path $webDir 'frpc\dist'

    if ($Force -or -not (Test-Path $frpsDist) -or -not (Test-Path $frpcDist)) {
        if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
            throw "找不到 npm，请先安装 Node.js 18+，或加 -SkipWeb 跳过前端构建"
        }
        Write-Step "构建 frps / frpc 管理面板前端"
        Push-Location $webDir
        try {
            Write-Host "  npm ci ..."
            if ((Invoke-Native -File 'npm.cmd' -Arguments @('ci','--no-audit','--no-fund') -Quiet) -ne 0) {
                throw "npm ci 失败"
            }

            foreach ($ws in @('frps', 'frpc')) {
                Write-Host "  构建 $ws ..."
                if ((Invoke-Native -File 'npm.cmd' -Arguments @('run','build-only','--workspace',$ws) -Quiet) -ne 0) {
                    throw "构建 $ws 前端失败"
                }
            }
        } finally { Pop-Location }
        Write-Host "  前端已生成（会被编译进 frps/frpc 二进制）"
    } else {
        Write-Host "  前端已存在，跳过构建（要重新构建请加 -Force）"
    }
}

# ------------------------------------------------------------- 3. 编译两个 exe
Write-Step "编译 frps.exe / frpc.exe"
$go = Find-Go
if (-not $go) {
    throw "找不到 go.exe。请安装 Go 1.25+ 并加入 PATH，或把 Go 解压到 $RepoRoot\tools\go"
}
Write-Host "  Go: $go"
& $go version

$env:CGO_ENABLED = '0'
$env:GOOS        = 'windows'
$env:GOARCH      = 'amd64'
$env:GOPROXY     = $GoProxy
$env:GOSUMDB     = 'sum.golang.google.cn'

New-Item -ItemType Directory -Force -Path $FrpBinDir | Out-Null

Push-Location $FrpSrcDir
try {
    Write-Host "  下载依赖 ..."
    if ((Invoke-Native -File $go -Arguments @('mod','download','all') -Quiet) -ne 0) {
        throw "go mod download 失败（网络不通可以改 -GoProxy）"
    }

    $tags = if ($SkipWeb) { 'frps,noweb' } else { 'frps' }
    Write-Host "  go build frps ..."
    if ((Invoke-Native -File $go -Arguments @('build','-trimpath','-buildvcs=false','-ldflags','-s -w','-tags',$tags,'-o','bin/frps.exe','./cmd/frps') -Quiet) -ne 0) {
        throw "编译 frps 失败"
    }

    $tags = if ($SkipWeb) { 'frpc,noweb' } else { 'frpc' }
    Write-Host "  go build frpc ..."
    if ((Invoke-Native -File $go -Arguments @('build','-trimpath','-buildvcs=false','-ldflags','-s -w','-tags',$tags,'-o','bin/frpc.exe','./cmd/frpc') -Quiet) -ne 0) {
        throw "编译 frpc 失败"
    }
} finally { Pop-Location }

Write-Step "结果"
foreach ($f in @('frps.exe', 'frpc.exe')) {
    $p = Join-Path $FrpBinDir $f
    if (-not (Test-Path $p)) { throw "没有生成 $p" }
    Write-Host ("  {0,-12} {1,10:N1} MB" -f $f, ((Get-Item $p).Length / 1MB))
}
Write-Host "  版本: $(& (Join-Path $FrpBinDir 'frps.exe') --version)"
Write-Host "`n完成 ✔" -ForegroundColor Green
