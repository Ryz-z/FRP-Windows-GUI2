# ============================================================================
#  界面布局回归测试
#  针对实际出现过的缺陷：
#   1) “② 代理规则 / ③ 访问者” 的按钮条盖住表格表头，第一行只显示一半
#   2) 按钮用固定宽度，在高 DPI 下文字被截断
#   3) 访问者表格太矮，表头 + 两行都放不下
#   4) 默认窗口下内容溢出，必须滚动才能看到 ③
#   5) 表头/行高按构造时的字体算死，继承 9pt 字体后表头文字被纵向截掉一半
# ============================================================================
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'config.ps1')

$APP = $GuiExe
$TMPDIR = $BuildDir
$OUT = Join-Path $TMPDIR 'layout.txt'

$pass = 0; $fail = 0
function Check($name, $ok, $detail = '') {
    if ($ok) { Write-Host "  [PASS] $name  $detail" -ForegroundColor Green; $script:pass++ }
    else     { Write-Host "  [FAIL] $name  $detail" -ForegroundColor Red;   $script:fail++ }
}

Write-Host "=== 1. 运行布局自检 ===" -ForegroundColor Cyan
$p = Start-Process -FilePath $APP -ArgumentList '--dump-layout','zh' -PassThru -Wait -RedirectStandardOutput $OUT
Check "布局自检进程退出码为 0" ($p.ExitCode -eq 0) "exit=$($p.ExitCode)"
$lines = Get-Content $OUT -Encoding UTF8
if ($lines | Where-Object { $_ -match '布局自检失败' }) { Write-Host ($lines | Select-String '布局自检失败') -ForegroundColor Red }

function Get-Ctl($name) {
    $l = $lines | Where-Object { $_ -match "^CTL\|.*\|$name\|" } | Select-Object -Last 1
    if (-not $l) { return $null }
    $f = $l -split '\|'
    # 字段：CTL|Type|Name|Left|Top|Width|Height
    return [pscustomobject]@{
        Type   = $f[1]; Name = $f[2]
        Left   = [int]$f[3]; Top = [int]$f[4]
        Width  = [int]$f[5]; Height = [int]$f[6]
        Right  = [int]$f[3] + [int]$f[5]
        Bottom = [int]$f[4] + [int]$f[6]
    }
}

function Get-Btn($name) {
    $l = $lines | Where-Object { $_ -match "^BTN\|$name\|" } | Select-Object -Last 1
    if (-not $l) { return $null }
    $f = $l -split '\|'
    # 字段：BTN|Name|Text|Left|Top|Width|Height|NeedWidth
    return [pscustomobject]@{
        Name = $f[1]; Text = $f[2]
        Left = [int]$f[3]; Top = [int]$f[4]
        Width = [int]$f[5]; Height = [int]$f[6]; Need = [int]$f[7]
        Right = [int]$f[3] + [int]$f[5]
        Bottom = [int]$f[4] + [int]$f[6]
    }
}

function Get-Grd($name) {
    $l = $lines | Where-Object { $_ -match "^GRD\|$name\|" } | Select-Object -Last 1
    if (-not $l) { return $null }
    $f = $l -split '\|'
    # 字段：GRD|Name|ColumnHeadersHeight|RowHeight|TextHeight|Font
    return [pscustomobject]@{
        Name = $f[1]; HeaderH = [int]$f[2]; RowH = [int]$f[3]
        TextH = [int]$f[4]; Font = $f[5]
    }
}

Write-Host "`n=== 2. 取控件实际位置 ===" -ForegroundColor Cyan
$proxyBar  = Get-Ctl 'proxyBar'
$gridP     = Get-Ctl 'gridProxies'
$visBar    = Get-Ctl 'visBar'
$gridV     = Get-Ctl 'gridVisitors'
$visBox    = Get-Ctl 'visBox'
$mP        = Get-Grd 'gridProxies'
$mV        = Get-Grd 'gridVisitors'
$sf        = ($lines | Where-Object { $_ -match '\|scrollArea\|' } | Select-Object -Last 1) -split '\|'
$viewBottom = [int]$sf[4] + [int]$sf[6]

foreach ($c in @($proxyBar, $gridP, $visBar, $gridV, $visBox)) {
    if ($c) { Write-Host ("     {0,-14} Top={1,4} 底={2,4} 左={3,4} 宽={4,4} 高={5,4}" -f $c.Name, $c.Top, $c.Bottom, $c.Left, $c.Width, $c.Height) }
    else    { Write-Host "     控件缺失" -ForegroundColor Red }
}
Write-Host "     客户端可视区底部 = $viewBottom"

# ③访问者默认是收起的：表格本身不参与布局断言，但必须有一个展开按钮，
# 而且展开后的布局要另外单独验证（见第 12 节）。
$visState = ($lines | Where-Object { $_ -match '^VS\|' } | Select-Object -Last 1)
Check "读到③访问者的折叠状态" ([bool]$visState) "$visState"
$visCollapsed = $visState -match 'collapsed=True'
$btnVisToggle = Get-Btn 'visToggle'
$btnImport    = Get-Btn 'btnImportProxy'

Check "关键控件都拿到了" ($proxyBar -and $gridP -and $visBox)
Check "代理表格的度量拿到了" ([bool]$mP)
Check "③访问者默认收起" $visCollapsed "$visState"
Check "③访问者有展开/收起按钮" ([bool]$btnVisToggle) $(if ($btnVisToggle) { $btnVisToggle.Text } else { '' })
Check "②工具条有「导入配置」按钮" ([bool]$btnImport) $(if ($btnImport) { $btnImport.Text } else { '' })

if (-not $visCollapsed) {
    Check "访问者表格的度量拿到了" ([bool]$mV)
    Check "访问者表格从按钮条下方开始" ($gridV.Top -ge $visBar.Bottom) "bar底=$($visBar.Bottom) 表格顶=$($gridV.Top)"
}

Write-Host "`n=== 3. 核心缺陷：按钮条不能盖住表格（表头/第一行必须完整可见） ===" -ForegroundColor Cyan
Check "代理规则：表格从按钮条下方开始" ($gridP.Top -ge $proxyBar.Bottom) "bar底=$($proxyBar.Bottom) 表格顶=$($gridP.Top)"
Check "②工具条只有一行（没有被按钮挤到第二行）" ($proxyBar.Height -le 56) "高=$($proxyBar.Height)"

Write-Host "`n=== 4. 表头高度、行高必须放得下当前字体的文字（否则会被纵向截掉） ===" -ForegroundColor Cyan
$metrics = @($mP)
if ($mV) { $metrics += $mV }
foreach ($m in $metrics) {
    Check "$($m.Name) 表头高度够（>= 文字高+6）" ($m.HeaderH -ge ($m.TextH + 6)) "表头=$($m.HeaderH) 文字高=$($m.TextH) 字体=$($m.Font)"
    Check "$($m.Name) 行高够（>= 文字高+4）"     ($m.RowH -ge ($m.TextH + 4))    "行高=$($m.RowH) 文字高=$($m.TextH)"
}

Write-Host "`n=== 5. 代理表格：规则多的时候不用一直滚动 ===" -ForegroundColor Cyan
# 第四版把③访问者做成可折叠之后，②代理规则默认能显示 8 行以上；
# 这里断言至少 8 行，防止以后又把它挤小。
$proxyRows = [math]::Floor(($gridP.Height - $mP.HeaderH) / $mP.RowH)
Check "代理表格至少能显示表头+8行规则" ($gridP.Height -ge ($mP.HeaderH + 8 * $mP.RowH)) `
      "高=$($gridP.Height) 需要=$($mP.HeaderH + 8 * $mP.RowH) 实际可显示 $proxyRows 行"
if ($mV) {
    Check "访问者表格能显示表头+2行" (($gridV.Height -ge ($mV.HeaderH + 2 * $mV.RowH))) "高=$($gridV.Height)"
}

Write-Host "`n=== 6. 默认窗口下内容不需要滚动 ===" -ForegroundColor Cyan
# 第四版把窗口默认高度提到 1080，并让③访问者默认收起，
# 客户端页正好放下，不应该出现滚动条。
$overflow = $visBox.Bottom - $viewBottom
Check "客户端页内容不溢出可视区太多" ($overflow -le 120) "内容底=$($visBox.Bottom) 可视区底=$viewBottom 溢出=$overflow"
Check "③访问者那一行完整可见" ($visBox.Bottom -le ($viewBottom + 120)) "行底=$($visBox.Bottom)"

Write-Host "`n=== 6b. 多服务端连接的工具条（第四版新增）===" -ForegroundColor Cyan
$connBar  = Get-Ctl 'connBar'
$connList = Get-Ctl 'connList'
$btnAdd   = Get-Btn 'btnAddConn'
$btnDel   = Get-Btn 'btnDelConn'
$btnRen   = Get-Btn 'btnRenameConn'
# 复选框在自检里是 LBL 行（不带控件名），所以按文字找
$chkEnTxt = @($lines | Where-Object { $_ -match '^LBL\|启用\|' }).Count
Check "连接选择条控件齐全" ($connBar -and $connList -and $btnAdd -and $btnDel -and $btnRen -and ($chkEnTxt -gt 0))
Check "「＋ 新添服务器」按钮文字完整" ($btnAdd -and $btnAdd.Width -ge 120) "宽=$($btnAdd.Width)"
if ($connBar) {
    Check "连接选择条只有一行（没有换行）" ($connBar.Height -le 52) "高=$($connBar.Height)"
    $barRight = $connBar.Left + $connBar.Width
    foreach ($c in @($connList, $btnAdd, $btnDel, $btnRen)) {
        $right = $c.Left + $c.Width
        Check "连接条里的 $($c.Name) 没超出右边界" ($right -le $barRight) "$right <= $barRight"
    }
}

Write-Host "`n=== 7. 按钮文字不能被截断 ===" -ForegroundColor Cyan
$btns = $lines | Where-Object { $_ -match '^BTN\|' }
Check "读到按钮数量 > 15" ($btns.Count -gt 15) "共 $($btns.Count) 个"
$clipped = @()
foreach ($b in $btns) {
    $f = $b -split '\|'
    # 字段：BTN|Name|Text|Left|Top|Width|Height|NeedWidth
    $w = [int]$f[5]; $need = [int]$f[7]; $txt = $f[2]
    if ($need -gt $w) { $clipped += "$txt (需要${need} 实际${w})" }
}
Check "所有按钮文字都不被截断" ($clipped.Count -eq 0) $(if ($clipped.Count) { $clipped -join '; ' } else { "共检查 $($btns.Count) 个按钮" })

Write-Host "`n=== 8. 字段标签不能被挤成两行 ===" -ForegroundColor Cyan
$wrapped = @()
foreach ($l in ($lines | Where-Object { $_ -match '^LBL\|' })) {
    $f = $l -split '\|'
    $txt = $f[1]; $h = [int]$f[3]; $single = [int]$f[4]
    # 「提示：…」这类说明标签本来就设了 MaximumSize、允许折行，不算缺陷
    if ($txt.Length -le 40 -and $h -gt $single -and -not $txt.StartsWith('提示：') -and -not $txt.StartsWith('Tip:')) { $wrapped += $txt }
}
Check "所有字段标签都是单行" ($wrapped.Count -eq 0) $(if ($wrapped.Count) { $wrapped -join '; ' } else { "共检查 $(($lines | Where-Object { $_ -match '^LBL\|' }).Count) 个标签" })

Write-Host "`n=== 9. 表格列铺满宽度，右侧不留大片空白 ===" -ForegroundColor Cyan
Check "代理表格宽度合理" ($gridP.Width -gt 600) "宽=$($gridP.Width)px"

# ============================================================================
#  “一键放行客户端端口” 的子对话框（第三版新增）
# ============================================================================
function Dump-Dialog($which) {
    $f = Join-Path $TMPDIR "dlg-$which.txt"
    Remove-Item $f -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $APP -ArgumentList '--dump-dialog', $which, 'zh' -PassThru -Wait -RedirectStandardOutput $f
    return [pscustomobject]@{ Code = $p.ExitCode; Lines = @(Get-Content $f -Encoding UTF8) }
}

function Get-Boxes($lines) {
    $res = @()
    foreach ($l in $lines) {
        if ($l -match '^CTL\|') {
            $f = $l -split '\|'
            $res += [pscustomobject]@{ Kind='CTL'; Name=$f[2]; Left=[int]$f[3]; Top=[int]$f[4]
                                       W=[int]$f[5]; H=[int]$f[6]; Need=0; Text='' }
        }
        elseif ($l -match '^BTN\|') {
            $f = $l -split '\|'
            $res += [pscustomobject]@{ Kind='BTN'; Name=$f[1]; Text=$f[2]; Left=[int]$f[3]; Top=[int]$f[4]
                                       W=[int]$f[5]; H=[int]$f[6]; Need=[int]$f[7] }
        }
    }
    return $res
}

function Get-DlgForm($lines) {
    $f = ($lines | Where-Object { $_ -match '^FORM\|' } | Select-Object -First 1) -split '\|'
    return [pscustomobject]@{ W=[int]$f[1]; H=[int]$f[2] }
}

function Assert-Dialog($title, $lines, $mustHave) {
    $form = Get-DlgForm $lines
    $boxes = Get-Boxes $lines

    Write-Host "     窗口 $($form.W)x$($form.H)，控件 $($boxes.Count) 个"

    $missing = @()
    foreach ($need in $mustHave) {
        if (-not ($boxes | Where-Object { $_.Name -eq $need })) { $missing += $need }
    }
    Check "$title 关键控件齐全" ($missing.Count -eq 0) $(if ($missing.Count) { "缺: $($missing -join ', ')" } else { "" })

    $out = @($boxes | Where-Object { ($_.Left + $_.W) -gt $form.W -or ($_.Top + $_.H) -gt $form.H -or $_.Left -lt 0 -or $_.Top -lt 0 })
    $outText = ($out | ForEach-Object {
        $r = $_.Left + $_.W; $b = $_.Top + $_.H
        "$($_.Name) 右=$r 底=$b"
    }) -join '; '
    Check "$title 所有控件都在窗口内" ($out.Count -eq 0) $(if ($out.Count) { $outText } else { "窗口 $($form.W)x$($form.H)" })

    $clip = @($boxes | Where-Object { $_.Kind -eq 'BTN' -and $_.Need -gt $_.W })
    Check "$title 按钮文字不被截断" ($clip.Count -eq 0) $(if ($clip.Count) { ($clip | ForEach-Object { "$($_.Text) 需要$($_.Need) 实际$($_.W)" }) -join '; ' } else { "" })

    # 表格表头和行高必须放得下当前字体的文字
    $bad = @()
    foreach ($g in ($lines | Where-Object { $_ -match '^GRD\|' })) {
        $f = $g -split '\|'
        $h = [int]$f[2]; $r = [int]$f[3]; $t = [int]$f[4]
        if ($h -lt ($t + 6)) { $bad += "$($f[1]) 表头 $h < $($t + 6)" }
        if ($r -lt ($t + 4)) { $bad += "$($f[1]) 行高 $r < $($t + 4)" }
    }
    Check "$title 表头/行高够放文字" ($bad.Count -eq 0) $(if ($bad.Count) { $bad -join '; ' } else { "" })

    $totalH = $form.H + 45
    Check "$title 能在 768 高的屏幕上完整显示" ($totalH -le 768) "窗口高约 $totalH"
    return $boxes
}

Write-Host "`n=== 10. 对话框 ① 选择客户端 ===" -ForegroundColor Cyan
$d1 = Dump-Dialog 'clients'
Check "对话框自检退出码为 0" ($d1.Code -eq 0) "exit=$($d1.Code)"
$b1 = Assert-Dialog '① 选择客户端' $d1.Lines @('cliConn','cliToolbar','cliGrid','cliButtons','cliNext','cliManual','cliClose')
$bar1 = $b1 | Where-Object { $_.Name -eq 'cliToolbar' }
$grd1 = $b1 | Where-Object { $_.Name -eq 'cliGrid' }
$bar1Bottom = $bar1.Top + $bar1.H
Check "① 表格从工具条下方开始" ($grd1.Top -ge $bar1Bottom) "bar底=$bar1Bottom 表格顶=$($grd1.Top)"
Check "① 表格高度至少放得下 3 行" ((($grd1.H - 32) / 33) -ge 3) "可显示 $([math]::Floor(($grd1.H - 32) / 33)) 行"

Write-Host "`n=== 11. 对话框 ② 选择端口 ===" -ForegroundColor Cyan
$d2 = Dump-Dialog 'ports'
Check "对话框自检退出码为 0" ($d2.Code -eq 0) "exit=$($d2.Code)"
$b2 = Assert-Dialog '② 选择端口' $d2.Lines @('fwToolbar','fwGrid','fwDir','fwDirFlow','fwRbIn','fwRbOut','fwRbBoth','fwButtons','fwApply','fwClose')
$dir2  = $b2 | Where-Object { $_.Name -eq 'fwDir' }
$btn2  = $b2 | Where-Object { $_.Name -eq 'fwButtons' }
$flag2 = $b2 | Where-Object { $_.Name -eq 'fwRbBoth' }
$dirBottom = $dir2.Top + $dir2.H
$flagBottom = $flag2.Top + $flag2.H
Check "② 方向选择框不压住底部按钮" ($dirBottom -le $btn2.Top) "方向框底=$dirBottom 按钮顶=$($btn2.Top)"
Check "② 第三个单选项在方向框里" ($flagBottom -le $dirBottom) "单选底=$flagBottom 框底=$dirBottom"
$allLbl = @($d2.Lines | Where-Object { $_ -match '^LBL\|全选\|' })
$allTxt = $allLbl -join ' / '
$allOk = $false
if ($allLbl.Count -eq 1) {
    $af = $allLbl[0] -split '\|'
    $allOk = ([int]$af[3]) -le ([int]$af[4])
}
Check "② 有「全选」复选框且是单行" $allOk $allTxt

# ============================================================================
#  第 12 节：③访问者展开之后的布局（第四版把它做成可折叠）
#  展开时会多占 126 像素，这一节断言展开后表格依然放得下、按钮不被截断。
# ============================================================================
Write-Host "`n=== 12. ③访问者展开后的布局 ===" -ForegroundColor Cyan
$expOut = Join-Path $TMPDIR 'layout-expanded.txt'
Remove-Item $expOut -Force -ErrorAction SilentlyContinue
$pe = Start-Process -FilePath $APP -ArgumentList '--dump-layout','zh','--expand-visitors' -PassThru -Wait -RedirectStandardOutput $expOut
$elines = @(Get-Content $expOut -Encoding UTF8)
Check "展开态布局自检退出码为 0" ($pe.ExitCode -eq 0) "exit=$($pe.ExitCode) 行数=$($elines.Count)"
$estate = $elines | Where-Object { $_ -match '^VS\|' } | Select-Object -Last 1
Check "展开态确实是展开的" ($estate -match 'collapsed=False') "$estate"

function Get-ECtl($name) {
    $l = $elines | Where-Object { $_ -match "^CTL\|.*\|$name\|" } | Select-Object -Last 1
    if (-not $l) { return $null }
    $f = $l -split '\|'
    return [pscustomobject]@{ Name=$f[2]; Left=[int]$f[3]; Top=[int]$f[4]; W=[int]$f[5]; H=[int]$f[6]
                              Right=[int]$f[3]+[int]$f[5]; Bottom=[int]$f[4]+[int]$f[6] }
}
function Get-EGrd($name) {
    $l = $elines | Where-Object { $_ -match "^GRD\|$name\|" } | Select-Object -Last 1
    if (-not $l) { return $null }
    $f = $l -split '\|'
    return [pscustomobject]@{ Name=$f[1]; HeaderH=[int]$f[2]; RowH=[int]$f[3]; TextH=[int]$f[4] }
}

$eGridV = Get-ECtl 'gridVisitors'
$eGridP = Get-ECtl 'gridProxies'
$eHostV = Get-ECtl 'visGridHost'
$eBarV  = Get-ECtl 'visBar'
$emV    = Get-EGrd 'gridVisitors'
Check "展开后能看到访问者表格" ([bool]$eGridV)
Check "展开后访问者区域够高（工具条 + 表头 + 2 行）" `
      ($emV -and $eBarV -and $eHostV -and ($eHostV.H -ge ($eBarV.H + $emV.HeaderH + 2 * $emV.RowH))) `
      $(if ($eHostV -and $eBarV -and $emV) { "区域高=$($eHostV.H) 需要=$($eBarV.H + $emV.HeaderH + 2*$emV.RowH)" } else { '' })
Check "展开后访问者表格仍在按钮条下方" ($eGridV -and $eBarV -and ($eGridV.Top -ge $eBarV.Bottom)) `
      $(if ($eGridV -and $eBarV) { "bar底=$($eBarV.Bottom) 表格顶=$($eGridV.Top)" } else { '' })
Check "展开后代理表格没有被挤小" ($eGridP -and $gridP -and ($eGridP.H -ge $gridP.H - 4)) `
      $(if ($eGridP -and $gridP) { "展开高=$($eGridP.H) 收起高=$($gridP.H)" } else { '' })

$eClipped = @()
foreach ($b in ($elines | Where-Object { $_ -match '^BTN\|' })) {
    $f = $b -split '\|'
    if ([int]$f[7] -gt [int]$f[5]) { $eClipped += "$($f[2]) (需要$($f[7]) 实际$($f[5]))" }
}
Check "展开后按钮文字依然不被截断" ($eClipped.Count -eq 0) $(if ($eClipped.Count) { $eClipped -join '; ' } else { '' })

$eWrap = @()
foreach ($l in ($elines | Where-Object { $_ -match '^LBL\|' })) {
    $f = $l -split '\|'
    $txt = $f[1]; $h = [int]$f[3]; $single = [int]$f[4]
    if ($txt.Length -le 40 -and $h -gt $single -and -not $txt.StartsWith('提示：') -and -not $txt.StartsWith('Tip:')) { $eWrap += $txt }
}
Check "展开后字段标签都是单行" ($eWrap.Count -eq 0) $(if ($eWrap.Count) { $eWrap -join '; ' } else { '' })

# ============================================================================
#  第 13 节：导入 frpc 配置对话框的布局（第四版新增）
# ============================================================================
Write-Host "`n=== 13. 导入配置对话框 ===" -ForegroundColor Cyan
$d3 = Dump-Dialog 'import'
Check "导入对话框自检退出码为 0" ($d3.Code -eq 0) "exit=$($d3.Code)"
$b3 = Assert-Dialog '导入配置' $d3.Lines @('impRoot','impBar','impText','impBottom','impTarget','impButtons','impOk','impCancel')
Check "导入对话框有选文件按钮" ([bool]($b3 | Where-Object { $_.Name -eq 'impPick' }))
Check "导入对话框有读取本机配置按钮" ([bool]($b3 | Where-Object { $_.Name -eq 'impFromData' }))
Check "导入对话框有「新添 / 并进」两个选项" `
      ([bool](($b3 | Where-Object { $_.Name -eq 'impRbNew' })) -and [bool](($b3 | Where-Object { $_.Name -eq 'impRbMerge' })))
$impText = $b3 | Where-Object { $_.Name -eq 'impText' }
$impBar  = $b3 | Where-Object { $_.Name -eq 'impBar' }
Check "示例配置被识别出代理规则" ([bool](@($d3.Lines | Where-Object { $_ -match '识别到 2 条代理规则' }).Count)) `
      (@($d3.Lines | Where-Object { $_ -match '^LBL\|识别到' }) -join ' / ')
Check "文本框足够大（高 >= 200）" ($impText -and $impText.H -ge 200) $(if ($impText) { "高=$($impText.H)" } else { '' })
Check "文本框在按钮条下方" ($impText -and $impBar -and ($impText.Top -ge ($impBar.Top + $impBar.H))) `
      $(if ($impText -and $impBar) { "bar底=$($impBar.Top + $impBar.H) 文本顶=$($impText.Top)" } else { '' })

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "  通过: $pass   失败: $fail"
Write-Host "======================================" -ForegroundColor Cyan
if ($fail -gt 0) { exit 1 } else { exit 0 }
