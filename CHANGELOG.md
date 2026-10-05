# 更新记录

## v1.0.0

> 版本号一直保持 1.0.0；下面按迭代顺序记录每一轮做了什么。

### 代理规则表格加高 + ③ 访问者可折叠

- 「② 代理规则」表格默认高度能让**一眼看到 9 条以上**规则（原来只有 4~5 条），
  新增规则基本不用滚动了
- 「③ 访问者」只在 stcp / xtcp 点对点穿透时才用得上，做成了**可折叠**：
  标题行是一个「③ 访问者（点此展开 / 收起）」按钮，收起时表格隐藏、
  那一行只占 44 像素，省下来的 126 像素全部归代理规则表格；展开后
  访问者表格依然放得下表头 + 2 行
- 主窗口默认高度 1000 → 1080，日志框 105 → 88 像素，把空间让给表格
- 信息行新增「共 N 条规则、M 条访问者」实时计数，添加 / 删除 / 导入后
  立即刷新

### 导入现成的 frpc 配置

- 「② 代理规则」工具条新增「导入配置」按钮，弹出导入对话框：
  - 「选择配置文件…」挑一个 `frpc.toml` / `frpc.ini` / `*.conf`
  - 「读取本机已保存的配置」直接从 `C:\ProgramData\FrpWin` 里挑
  - 或者**直接把配置内容粘进文本框**，边粘边识别（不需要先点解析）
- 识别内容：`serverAddr` / `serverPort` / `auth.token` / `transport.tls.enable` /
  `log.level` / `loginFailExit` / `webServer.*`，以及全部 `[[proxies]]`
  和 `[[visitors]]`
- 目标可选**「新添一个服务端连接」**或**「并进当前连接」**；重名的规则会
  自动改名（`name` → `name_2`），不覆盖已有规则
- 兼容性：
  - 点号键（`transport.tls.enable`）与下划线键（`server_addr`）都认
  - **老版 INI 格式**（`[common]` + `[ssh]` 这种段落）也认：段落的类型由
    段内的 `type` 键决定，段落名当规则名（没有 name 键时自动补 `auto-tcp1`）
  - `includes` 递归展开（相对路径按配置文件目录解析，带环路保护，最多 5 层）
  - 数组写法 `customDomains = ["a.com","b.com"]` 与逗号串都能读
- **界面外的参数不丢**：`healthCheck.*`、`loadBalancer.*`、`metadatas`、
  `transport.protocol` 等键原样存进 `ProxyItem.ExtraLines`，
  生成 `frpc-*.toml` 时原样写回
- 新增自检开关 `FrpWin.exe --import-frpc <文件>`，把识别结果按
  `CONN|PROXY|PEXTRA|VISITOR|EXTRA|FILE` 行打印出来，供自动化测试断言

### 多服务端连接（一个客户端连多个 frps）

- 「客户端」页的「① 连接设置」新增连接工具条：
  `服务端连接 [下拉框] [＋ 新添服务器] [－ 删除连接] [重命名] [管理界面账号] [☑ 启用]`
- 每个连接拥有**独立**的服务器地址 / 端口 / token / TLS 开关 / 日志级别 /
  管理界面端口，以及自己独立的代理规则表和访问者表；切换下拉框会先把当前
  填的内容存回上一个连接再读出新连接，来回切不丢东西
- frp 的 frpc 一个进程只能连一个服务端，所以「一对多」的实现是
  **每个连接生成一份独立的 `frpc-*.toml`、各起一个 frpc 进程**：
  `frpc.toml` / `frpc-2.toml` / `frpc-3.toml`；连接数变少时多余的配置文件自动删除
- 一次「保存并启动客户端」拉起全部已启用连接，状态栏汇总为
  「运行中 2/3 个连接 (PID …)」，日志每行带连接名前缀 `[家里 NAS] …`
- `--run frpc` 与 Windows 服务宿主走同一套逻辑，同样按连接多进程启动
- 「查看配置文件」「管理界面账号」作用于当前下拉框选中的那个连接
- 旧版单连接配置（`ui-settings.xml` 里平铺的 `ServerAddr` / `Token` / `Proxies` …）
  启动时自动迁移成「服务器 1」，代理规则与访问者一条不丢

### 英语模块补全

修掉「安装时选 English，装完程序里还是中文」这个缺陷。

- 安装程序在 `ssPostInstall` 把语言选择写进 `C:\ProgramData\FrpWin\language.txt`
  （`zh` / `en`）
- 程序启动按优先级定语言：① 界面里明确选过的 → ② `language.txt` → ③ 系统界面语言
- 引入翻译表（376 条）：源码里照旧写中文原文，窗口建好后整棵控件树
  由 `Loc.Apply(Control)` 换成当前语言；需要拼串的地方走带占位符的 `Loc.F(zh, args)`；
  日志与消息框走 `Loc.Whole(text)`
- 「使用说明 / 关于」页底部新增「界面语言」下拉框
  （跟随安装程序 / 中文 / English）与「保存语言」按钮，重启后整个界面切换
- 生成的 `frps.toml` / `frpc-*.toml` 注释、命令行输出也跟随语言
- 选择英文安装时放入的是 `README.en.txt`，开始菜单「使用说明」快捷方式指向它
- 新增自检开关 `FrpWin.exe --dump-texts zh|en|auto`，供自动化测试断言语言

### 一键放行客户端所需的防火墙端口

- 「服务端」页新增按钮：读取服务端 Dashboard API（`/api/clients`、
  `/api/proxy/{tcp,udp,http,https,tcpmux,stcp,xtcp,sudp}`），列出**当前连上来的客户端**
- 选中某个客户端后可查看它占用的端口清单，逐项勾选（含全选），
  选择放行方向（出站 / 进站 / 两者），一次调用 `netsh advfirewall` 批量添加规则
- 探活与清单生成可用命令行复现：`FrpWin.exe --probe-frps <端口> [用户] [密码]`
- 说明列的兜底：真实 frp v0.71 的 `/api/proxy/tcp` 返回体中 **没有 `localPort`**
  （本地端口属于客户端自己的配置，frps 不记录），因此该列回退为
  「转发到该客户端的内网服务」而不是留空
- 代理 → 客户端的关联以 `clientID` 为准（等于 `RawClientID` 或 `RunID`），
  取不到时退回 `user`

### 界面缺陷修复（历次迭代中发现并修复）

| 问题 | 根因 | 修复 |
|---|---|---|
| 表格第一行只显示一半 | 按钮条用 `Dock=Top` 配 `BringToFront()`，z 序反转导致表格先占满区域、按钮条又盖回表头 | 改用 `TableLayoutPanel` 明确分行 |
| 按钮文字被横向截断 | 按钮用固定像素宽度，高 DPI 下文字实际宽度超出固定值 | 全部改为 `AutoSize` |
| 表头文字被纵向截掉一半 | 表头高度在控件构造时按系统默认字体算死（`DisableResizing`），继承 9pt 字体后字体变高却不重算 | 表头改 `AutoSize`；行高按 `TextRenderer` 实测文字高度计算并监听 `FontChanged` |
| 访问者表格太矮 | 所在行高只有 150px，装不下表头 + 两行 | 调整行高，并按字体度量重新分配空间 |
| 表格右侧一大片空白 | 列宽固定且总和小于面板宽度 | 改 `Fill` 模式按权重铺满 |
| 字段标签被挤成两行 | 标签列 190px 装不下中英混排文本 | 加宽到 235px；连接设置改为一行两组 |
| 默认窗口需滚动才能看全 | 各分组框占用高度过大 | 收紧 `GroupBox` 多余内边距，腾出的高度给表格 |
| 单列 `TableLayoutPanel` 里控件溢出到窗体右侧 | 没写 `ColumnStyles` 时单列会退化成 AutoSize，最宽的兄弟控件把整列撑出去 | 统一加 `ColumnStyle(Percent, 100F)` |
| 防火墙对话框第二个单选项压住「应用」按钮 | `FlowLayoutPanel` 的 `AutoSize` + `Dock=Fill` 先按一行算高、之后才按实际宽度折行 | 改成三行 `TableLayoutPanel` |
| 首行复选框渲染成「正在编辑」样式 | 填完数据后 `CurrentCell` 仍停在第一格 | 填充后显式 `_grid.CurrentCell = $null` |
| 英文界面下按钮折成两行 | 英文词比中文长 | 缩短按钮文案（`Connection` / `Admin UI` / `+ Visitor` / `Install Service` …） |

### 工程

- 界面自检开关 `--dump-layout [语言] [--expand-visitors]`、
  `--screenshot <png> [页] [语言] [演示数据]`、
  `--dump-dialog <clients|ports|import> [语言]`、
  `--probe-frps <端口> [用户] [密码]`、`--import-frpc <文件>`、
  `--dump-texts zh|en|auto`，配套 **261 项** 自动化断言
- 九个测试套件：端到端隧道、界面布局、配置生成、前台运行、安装卸载、
  英语模块、导入配置、客户端端口放行
- `scripts/verify-all.ps1` 一键跑完
- 全部构建/测试脚本使用**相对路径**，clone 到任意目录可直接运行
- 翻译表由 `scripts/gen-loctable.ps1` 从 `scripts/i18n/*.txt` 生成，
  `update-i18n.ps1` 能从源码重新抓取中文文案并校验译文完整性

### 首个公开版本（历史记录）

- 全中文图形管理器 `FrpWin.exe`（.NET Framework 4.8 / WinForms，免运行时安装）
- 服务端 / 客户端双标签页，支持 tcp / udp / http / https / stcp / xtcp 六种代理与 visitor
- 支持把 frps / frpc 注册为 Windows 服务，开机自启、崩溃自动重启
- 一键放行 Windows 防火墙端口
- 实时日志显示；服务模式日志按 8 MB 轮转
- 编译时内置 frp 官方 Web 管理面板前端
- Inno Setup 安装包：桌面快捷方式、开始菜单、覆盖安装保护、卸载清理
