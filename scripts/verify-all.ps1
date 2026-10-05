# ============================================================================
#  最终全量验证：重新构建 + 跑完全部测试
#  每个套件都在独立子进程里跑，退出码可靠，脚本语法错误不会被误判为通过
# ============================================================================
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'config.ps1')
$HERE = $PSScriptRoot
$results = @()

function Test-ScriptSyntax($path) {
    # 先自己按 BOM 粗判一次：带中文的脚本必须是 UTF-8 with BOM，
    # 否则 PowerShell 5.1 会按 GBK 解析，报一堆莫名其妙的语法错误
    try {
        $b = [System.IO.File]::ReadAllBytes($path)
        if (-not ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)) {
            Write-Host "  [提示] $([System.IO.Path]::GetFileName($path)) 缺少 UTF-8 BOM" -ForegroundColor Yellow
            return 1
        }
    } catch { }

    $err = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$err)
    return @($err).Count
}

function RunSuite($name, $script, $scriptArgs = @()) {
    Write-Host "`n`n##########  $name  ##########" -ForegroundColor Yellow

    $syn = Test-ScriptSyntax $script
    if ($syn -gt 0) {
        Write-Host "  脚本语法错误 $syn 处，跳过执行" -ForegroundColor Red
        $script:results += [pscustomobject]@{ Suite = $name; ExitCode = 99; Seconds = 0; Note = "语法错误" }
        return
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @scriptArgs 2>&1
    $code = $LASTEXITCODE
    $sw.Stop()

    $out | ForEach-Object { Write-Host $_ }

    $script:results += [pscustomobject]@{
        Suite = $name; ExitCode = $code; Seconds = [int]$sw.Elapsed.TotalSeconds; Note = ""
    }
}

# 1. 重新编译图形管理器
Write-Host "##########  重新编译 FrpWin.exe  ##########" -ForegroundColor Yellow
Push-Location "$SrcDir\FrpWin"
dotnet build FrpWin.csproj -c Release -v m 2>&1 | Select-Object -Last 4 | ForEach-Object { Write-Host $_ }
Pop-Location

# 2. 重新打包
RunSuite "打包构建 build.ps1" "$HERE\build.ps1"

# 3. 各测试套件
RunSuite "端到端隧道 e2e-test"     "$HERE\e2e-test.ps1"
RunSuite "界面布局 test-layout"    "$HERE\test-layout.ps1"
RunSuite "配置生成 test-config"    "$HERE\test-config.ps1"
RunSuite "前台运行 test-runmode"   "$HERE\test-runmode.ps1"
RunSuite "安装卸载 test-installer" "$HERE\test-installer.ps1"
RunSuite "英语模块 test-i18n"      "$HERE\test-i18n.ps1"
RunSuite "导入配置 test-import"     "$HERE\test-import.ps1"
RunSuite "客户端端口放行 test-firewall-api" "$HERE\test-firewall-api.ps1"

# 4. 汇总
Write-Host "`n`n==================================================" -ForegroundColor Cyan
Write-Host "              最终验证汇总" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
foreach ($r in $results) {
    $mark = if ($r.ExitCode -eq 0) { "通过" } else { "失败" }
    $color = if ($r.ExitCode -eq 0) { "Green" } else { "Red" }
    Write-Host ("  {0,-26} {1,-6} {2,4}s  {3}" -f $r.Suite, $mark, $r.Seconds, $r.Note) -ForegroundColor $color
}
$failed = @($results | Where-Object { $_.ExitCode -ne 0 }).Count
Write-Host "--------------------------------------------------" -ForegroundColor Cyan
if ($failed -eq 0) { Write-Host "  全部通过 ✔" -ForegroundColor Green } else { Write-Host "  有 $failed 项失败 ✘" -ForegroundColor Red }

Write-Host "`n========== 第四版 成品目录 ==========" -ForegroundColor Cyan
Get-ChildItem $DistDir -Recurse -File | ForEach-Object {
    Write-Host ("  {0,-46} {1,10:N2} MB" -f $_.FullName.Substring($DistDir.Length + 1), ($_.Length / 1MB))
}

Write-Host "`n========== 残留进程检查 ==========" -ForegroundColor Cyan
$r = Get-Process FrpWin,frps,frpc -ErrorAction SilentlyContinue
if ($r) { $r | Select-Object Id,ProcessName | Format-Table -AutoSize } else { Write-Host "  无残留 ✔" }

exit $failed
