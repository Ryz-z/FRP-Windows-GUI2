# ============================================================================
#  英语模块回归测试
#
#  1) 静态覆盖：源码里出现的每一句中文，翻译表里都必须有对应译文
#  2) 运行期验证：英文模式下把界面文字全部导出来，断言不再出现中文
#  3) 语言优先级：安装程序写的 language.txt < 界面里选过的 ui-settings.xml
# ============================================================================
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'config.ps1')

$APP  = $GuiExe
$SRC  = Join-Path $SrcDir 'FrpWin'
$DATA = Join-Path $env:ProgramData 'FrpWin'
$TMPDIR = Join-Path $BuildDir 'i18n'
New-Item -ItemType Directory -Force -Path $TMPDIR | Out-Null

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

# ---------------------------------------------------------------------------
Write-Host "`n=== 1. 静态覆盖：每句中文都得有译文 ===" -ForegroundColor Cyan
$sources = Get-ChildItem "$SRC\*.cs" | Where-Object { $_.Name -notin @('Loc.cs','LocTable.cs') }
$literals = New-Object System.Collections.Generic.HashSet[string]
foreach ($f in $sources) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]; $t = $line.Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('///') -or $t.StartsWith('*')) { continue }
        foreach ($m in [regex]::Matches($line, '"((?:[^"\\]|\\.)*)"')) {
            $lit = $m.Groups[1].Value
            if ($lit -match '[\u4e00-\u9fff]') { [void]$literals.Add($lit) }
        }
    }
}
Check "源码里有中文文案" ($literals.Count -gt 200) "共 $($literals.Count) 句"

$table = [System.IO.File]::ReadAllText("$SRC\LocTable.cs", [System.Text.Encoding]::UTF8)
$missing = @()
foreach ($lit in $literals) {
    # 表里是成对写的： "原文", "译文",
    if (-not $table.Contains('"' + $lit + '",')) { $missing += $lit }
}
Check "所有中文文案都有译文" ($missing.Count -eq 0) $(if ($missing.Count) { "缺 $($missing.Count) 条，例如：" + ($missing[0..([Math]::Min(2,$missing.Count-1))] -join ' / ') } else { "共 $($literals.Count) 句" })

# 中英占位符数量必须一致，否则 Loc.F 会抛异常或漏参数
$badPlaceholder = @()
foreach ($m in [regex]::Matches($table, '(?m)^\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)",\s*$')) {
    $zh = $m.Groups[1].Value; $en = $m.Groups[2].Value
    $nz = ([regex]::Matches($zh, '\{\d+\}')).Count
    $ne = ([regex]::Matches($en, '\{\d+\}')).Count
    if ($nz -ne $ne) { $badPlaceholder += "$zh  ({0}×$nz / en×$ne)" }
}
Check "中英占位符数量一致" ($badPlaceholder.Count -eq 0) $(if ($badPlaceholder.Count) { $badPlaceholder[0] } else { "" })

# ---------------------------------------------------------------------------
function Run-Dump([string]$lang) {
    $out = Join-Path $TMPDIR "texts-$lang.txt"
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $APP -ArgumentList '--dump-texts', $lang -PassThru -Wait -RedirectStandardOutput $out
    return [pscustomobject]@{ Code = $p.ExitCode; Lines = @(Get-Content $out -Encoding UTF8) }
}

Write-Host "`n=== 2. 英文模式下界面里不能再出现中文 ===" -ForegroundColor Cyan
$en = Run-Dump 'en'
Check "导出退出码为 0" ($en.Code -eq 0) "exit=$($en.Code)"
Check "导出的是英文" ((@($en.Lines | Where-Object { $_ -eq 'LANG|en' }).Count) -eq 1) ""

# 允许保留的中文：语言名本身、以及用户在设置里自己起的连接名（属于用户数据）
$left = @($en.Lines | Where-Object {
    $_ -match '[\u4e00-\u9fff]' -and $_ -notmatch '\|langCombo\|' -and $_ -notmatch '\|connList\|'
})
Check "英文界面里没有漏翻的中文" ($left.Count -eq 0) $(if ($left.Count) { ($left | Select-Object -First 3) -join ' ;; ' } else { "共检查 $($en.Lines.Count) 行" })

Check "主窗口标题已英文化" ([bool](@($en.Lines | Where-Object { $_ -like '*FrpWin Tunnel Manager*' }).Count))
Check "托盘菜单已英文化" ([bool](@($en.Lines | Where-Object { $_ -like 'MENU|main|Exit*' }).Count))
Check "关于页正文已英文化" ([bool](@($en.Lines | Where-Object { $_ -like 'ABOUT|*Windows front end for frp*' }).Count))
Check "服务端页按钮已英文化" ([bool](@($en.Lines | Where-Object { $_ -like '*|Open client ports' }).Count))
Check "客户端页新增按钮已英文化" ([bool](@($en.Lines | Where-Object { $_ -like '*|+ Add server' }).Count))
Check "对话框已英文化" ([bool](@($en.Lines | Where-Object { $_ -like '*|Next: pick ports*' }).Count))

# 「导入 frpc 配置」对话框也要全英文（第四版新增）
$impEn = Join-Path $TMPDIR 'dlg-import-en.txt'
Remove-Item $impEn -Force -ErrorAction SilentlyContinue
$pImp = Start-Process -FilePath $APP -ArgumentList '--dump-dialog','import','en' -PassThru -Wait -RedirectStandardOutput $impEn
$impLines = @(Get-Content $impEn -Encoding UTF8)
Check "导入对话框导出成功" ($pImp.ExitCode -eq 0) "exit=$($pImp.ExitCode)"
$impLeft = @($impLines | Where-Object { $_ -match '[\u4e00-\u9fff]' })
Check "导入对话框里没有漏翻的中文" ($impLeft.Count -eq 0) $(if ($impLeft.Count) { ($impLeft | Select-Object -First 2) -join ' ;; ' } else { "共检查 $($impLines.Count) 行" })
Check "导入对话框按钮已英文化" ([bool](@($impLines | Where-Object { $_ -like '*|Import|*' }).Count))

Write-Host "`n=== 3. 中文模式下界面确实是中文 ===" -ForegroundColor Cyan
$zh = Run-Dump 'zh'
Check "导出的是中文" ((@($zh.Lines | Where-Object { $_ -eq 'LANG|zh' }).Count) -eq 1) ""
Check "主窗口标题是中文" ([bool](@($zh.Lines | Where-Object { $_ -like '*内网穿透管理器*' }).Count))
Check "服务端页按钮是中文" ([bool](@($zh.Lines | Where-Object { $_ -like '*|一键放行客户端端口' }).Count))
Check "客户端页新增按钮是中文" ([bool](@($zh.Lines | Where-Object { $_ -like '*|＋ 新添服务器' }).Count))

# ---------------------------------------------------------------------------
Write-Host "`n=== 4. 语言优先级：安装程序选择 vs 界面里选过 ==" -ForegroundColor Cyan
$SET = Join-Path $DATA 'ui-settings.xml'
$LANG = Join-Path $DATA 'language.txt'
$backup = Join-Path $TMPDIR 'ui-settings.bak'
Copy-Item $SET $backup -Force -ErrorAction SilentlyContinue
$langBackup = Join-Path $TMPDIR 'language.bak'
$hadLang = Test-Path $LANG
if ($hadLang) { Copy-Item $LANG $langBackup -Force }

function Set-SettingsLanguage([string]$lang) {
    $t = [System.IO.File]::ReadAllText($SET, [System.Text.Encoding]::UTF8)
    if ($t -match '<Language>') { $t = $t -replace '<Language>[^<]*</Language>', "<Language>$lang</Language>" }
    else { $t = $t -replace '(<FrpWinSettings[^>]*>)', "`$1`n  <Language>$lang</Language>" }
    [System.IO.File]::WriteAllText($SET, $t, (New-Object System.Text.UTF8Encoding $false))
}

# 4.1 安装程序选了英文 → language.txt=en，界面设置为 auto → 应该出英文
[System.IO.File]::WriteAllText($LANG, "en`r`n", (New-Object System.Text.UTF8Encoding $false))
Set-SettingsLanguage 'auto'
$r1 = Run-Dump 'auto'
Check "安装时选英文 → 界面英文" ([bool](@($r1.Lines | Where-Object { $_ -eq 'LANG|en' }).Count)) "LANG=$(@($r1.Lines | Where-Object { $_ -like 'LANG|*' })[0])"

# 4.2 安装程序选了中文 → language.txt=zh，界面设置为 auto → 应该出中文
[System.IO.File]::WriteAllText($LANG, "zh`r`n", (New-Object System.Text.UTF8Encoding $false))
$r2 = Run-Dump 'auto'
Check "安装时选中文 → 界面中文" ([bool](@($r2.Lines | Where-Object { $_ -eq 'LANG|zh' }).Count)) "LANG=$(@($r2.Lines | Where-Object { $_ -like 'LANG|*' })[0])"

# 4.3 界面里明确选过英文 → 即使 language.txt=zh，也出英文
[System.IO.File]::WriteAllText($LANG, "zh`r`n", (New-Object System.Text.UTF8Encoding $false))
Set-SettingsLanguage 'en'
$r3 = Run-Dump 'auto'
Check "界面里选过英文 → 覆盖安装程序的选择" ([bool](@($r3.Lines | Where-Object { $_ -eq 'LANG|en' }).Count)) "LANG=$(@($r3.Lines | Where-Object { $_ -like 'LANG|*' })[0])"

# 4.4 界面里明确选过中文 → 即使 language.txt=en，也出中文
[System.IO.File]::WriteAllText($LANG, "en`r`n", (New-Object System.Text.UTF8Encoding $false))
Set-SettingsLanguage 'zh'
$r4 = Run-Dump 'auto'
Check "界面里选过中文 → 覆盖安装程序的选择" ([bool](@($r4.Lines | Where-Object { $_ -eq 'LANG|zh' }).Count)) "LANG=$(@($r4.Lines | Where-Object { $_ -like 'LANG|*' })[0])"

# 收尾：还原设置
if (Test-Path $backup) { Copy-Item $backup $SET -Force }
if ($hadLang) { Copy-Item $langBackup $LANG -Force } else { Remove-Item $LANG -Force -ErrorAction SilentlyContinue }

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
