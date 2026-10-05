# ============================================================================
#  把 i18n-edits-*.txt 里的「搜索 → 替换」逐条应用到 src\FrpWin 的源码上。
#  块格式：
#     @@@文件名.cs
#     <<<
#     搜索文本（可多行，原样匹配）
#     >>>
#     替换文本（可多行）
# ============================================================================
param([switch]$WhatIf)

. (Join-Path $PSScriptRoot 'config.ps1')

$SRC = Join-Path $SrcDir 'FrpWin'
$files = Get-ChildItem (Join-Path $I18nDir 'i18n-edits-*.txt') | Sort-Object Name
if (-not $files) { Write-Host "找不到编辑文件"; exit 1 }

# ---- 解析所有块 ----
$edits = @()
foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
    $target = $null; $mode = ''; $search = @(); $replace = @()
    $flush = {
        if ($target -and $search.Count -gt 0) {
            $script:edits += [pscustomobject]@{
                File    = $target
                Search  = ($search -join "`n")
                Replace = ($replace -join "`n")
                Source  = $file.Name
            }
        }
    }
    foreach ($line in $lines) {
        if ($line.StartsWith('@@@')) {
            & $flush
            $target = $line.Substring(3).Trim(); $mode = ''; $search = @(); $replace = @()
            continue
        }
        if ($line -eq '<<<') { $mode = 's'; continue }
        if ($line -eq '>>>') { $mode = 'r'; continue }
        if ($mode -eq 's') { $search += $line }
        elseif ($mode -eq 'r') { $replace += $line }
    }
    & $flush
}

Write-Host "共解析出 $($edits.Count) 条替换规则"

# ---- 按文件应用 ----
$failed = 0
foreach ($group in ($edits | Group-Object File)) {
    $path = Join-Path $SRC $group.Name
    if (-not (Test-Path $path)) { Write-Host "  [跳过] 找不到 $path" -ForegroundColor Red; $script:failed++; continue }

    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $applied = 0; $missed = @()
    foreach ($e in $group.Group) {
        if ($text.Contains($e.Search)) {
            $text = $text.Replace($e.Search, $e.Replace)
            $applied++
        } else {
            $missed += ($e.Search -split "`n")[0]
        }
    }

    if (-not $WhatIf) {
        [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))
    }
    $color = if ($missed.Count) { 'Yellow' } else { 'Green' }
    Write-Host ("  {0,-26} 应用 {1}/{2}" -f $group.Name, $applied, $group.Group.Count) -ForegroundColor $color
    foreach ($m in $missed) { Write-Host "      [未匹配] $m" -ForegroundColor Yellow; $script:failed++ }
}

Write-Host ""
if ($failed -gt 0) { Write-Host "有 $failed 条没匹配上，请检查" -ForegroundColor Yellow; exit 1 }
Write-Host "全部替换成功" -ForegroundColor Green
exit 0
