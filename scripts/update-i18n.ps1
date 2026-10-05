# ============================================================================
#  重建翻译表：
#    1) 从源码里重新抓出所有中文文案（顺序固定 = 首次出现顺序）
#    2) 用现有 i18n-pairs.txt 做「原文 → 译文」字典
#    3) 叠加 i18n-overrides.txt 里的新增/修正译文
#    4) 按新顺序写出 i18n-pairs.txt，再生成 LocTable.cs
#  缺译文的条目会被列出来，脚本返回 1，什么也不写。
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'config.ps1')

$SRC   = Join-Path $SrcDir 'FrpWin'
# 注意：PowerShell 变量不区分大小写，$ZH 与 $zh 是同一个变量，所以这里用另一个名字
$ZH_FILE = Join-Path $I18nDir 'i18n-zh.txt'
$PAIRS = Join-Path $I18nDir 'i18n-pairs.txt'
$OVER  = Join-Path $I18nDir 'i18n-overrides.txt'

# ---- 1. 抓中文文案 ----
$files = Get-ChildItem "$SRC\*.cs" | Where-Object { $_.Name -notin @('Loc.cs','LocTable.cs') }
$seen = New-Object System.Collections.Generic.HashSet[string]
$zh = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]; $t = $line.Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('///') -or $t.StartsWith('*')) { continue }
        foreach ($m in [regex]::Matches($line, '"((?:[^"\\]|\\.)*)"')) {
            $lit = $m.Groups[1].Value
            if ($lit -match '[\u4e00-\u9fff]' -and $seen.Add($lit)) { $zh.Add($lit) }
        }
    }
}
Write-Host "源码里共 $($zh.Count) 句中文文案"

# ---- 2. 现有译文 ----
$map = @{}
if (Test-Path $PAIRS) {
    $old = [System.IO.File]::ReadAllLines($PAIRS, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i + 1 -lt $old.Count; $i += 2) { $map[$old[$i]] = $old[$i + 1] }
}
Write-Host "现有译文 $($map.Count) 条"

# ---- 3. 叠加修正 ----
if (Test-Path $OVER) {
    $ov = [System.IO.File]::ReadAllLines($OVER, [System.Text.Encoding]::UTF8)
    $n = 0
    for ($i = 0; $i + 1 -lt $ov.Count; $i += 2) {
        if ($ov[$i].Trim().Length -eq 0) { continue }
        $map[$ov[$i]] = $ov[$i + 1]; $n++
    }
    Write-Host "叠加修正 $n 条"
}

# ---- 4. 输出 ----
$missing = @($zh | Where-Object { -not $map.ContainsKey($_) })
if ($missing.Count -gt 0) {
    Write-Host "`n有 $($missing.Count) 句还没有译文：" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  $_" }
    Write-Host "`n请把它们按「中文一行 / 英文一行」的格式加到 $OVER 里再跑一次" -ForegroundColor Yellow
    exit 1
}

[System.IO.File]::WriteAllLines($ZH_FILE, $zh, (New-Object System.Text.UTF8Encoding $false))
$out = New-Object System.Collections.Generic.List[string]
foreach ($z in $zh) { $out.Add($z); $out.Add($map[$z]) }
[System.IO.File]::WriteAllLines($PAIRS, $out, (New-Object System.Text.UTF8Encoding $false))
Write-Host "已更新 i18n-zh.txt 与 i18n-pairs.txt（$($zh.Count) 条）" -ForegroundColor Green

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'gen-loctable.ps1')
exit $LASTEXITCODE
