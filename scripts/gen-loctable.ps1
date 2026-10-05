# ============================================================================
#  由 scripts\i18n\i18n-zh.txt + scripts\i18n\i18n-pairs.txt 生成 src\FrpWin\LocTable.cs
#  校验：pairs 的行数必须是 zh 的 2 倍，而且偶数行的原文必须和 zh 一一对应。
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'config.ps1')

$ZH    = Join-Path $I18nDir 'i18n-zh.txt'
$PAIRS = Join-Path $I18nDir 'i18n-pairs.txt'
$OUT   = Join-Path $SrcDir 'FrpWin\LocTable.cs'

$zh = [System.IO.File]::ReadAllLines($ZH, [System.Text.Encoding]::UTF8)
$pairs = [System.IO.File]::ReadAllLines($PAIRS, [System.Text.Encoding]::UTF8)

Write-Host "原文 $($zh.Count) 条，译文行 $($pairs.Count) 行"
if ($pairs.Count -ne $zh.Count * 2) {
    Write-Host "行数不对：应该是 $($zh.Count * 2) 行" -ForegroundColor Red
    exit 1
}

$bad = 0
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('namespace FrpWin')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    /// <summary>')
[void]$sb.AppendLine('    /// 翻译表：中文原文 → 英文译文，一对一行。')
[void]$sb.AppendLine('    /// 原文里的 {0} {1} 是给 Loc.F 用的占位符，英文里必须一一对应。')
[void]$sb.AppendLine('    /// 这张表由 build\i18n-zh.txt 与 build\i18n-pairs.txt 生成，')
[void]$sb.AppendLine('    /// 改文案请改那两个文件，然后重新跑 build\gen-loctable.ps1。')
[void]$sb.AppendLine('    /// </summary>')
[void]$sb.AppendLine('    internal static partial class Loc')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        private static readonly string[] Pairs =')
[void]$sb.AppendLine('        {')

for ($i = 0; $i -lt $zh.Count; $i++) {
    $z = $zh[$i]
    $p = $pairs[$i * 2]
    if ($z -ne $p) {
        Write-Host ("  [错位] 第 {0} 条：原文「{1}」译文行却是「{2}」" -f ($i + 1), $z, $p) -ForegroundColor Red
        $bad++
        if ($bad -gt 5) { break }
        continue
    }
    $e = $pairs[$i * 2 + 1]

    # 只做最基本的合法性检查：不能有没转义的双引号，也不能有 C# 不认识的转义
    if ($z -match '(?<!\\)"') { Write-Host "  [含裸引号] 原文：$z" -ForegroundColor Red; $bad++; continue }
    if ($e -match '(?<!\\)"') { Write-Host "  [含裸引号] 译文：$e" -ForegroundColor Red; $bad++; continue }
    if ($z -match '(?<!\\)\\(?![\\"''0abfnrtvux])') { Write-Host "  [非法转义] 原文：$z" -ForegroundColor Red; $bad++; continue }
    if ($e -match '(?<!\\)\\(?![\\"''0abfnrtvux])') { Write-Host "  [非法转义] 译文：$e" -ForegroundColor Red; $bad++; continue }

    [void]$sb.AppendLine('            "' + $z + '", "' + $e + '",')
}

[void]$sb.AppendLine('        };')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')

if ($bad -gt 0) { Write-Host "有 $bad 处问题，未生成" -ForegroundColor Red; exit 1 }

[System.IO.File]::WriteAllText($OUT, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host "已生成 $OUT（$($zh.Count) 条译文）" -ForegroundColor Green
exit 0
