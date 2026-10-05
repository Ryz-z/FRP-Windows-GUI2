using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FrpWin
{
    public sealed partial class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly FrpRunner _serverRunner = new FrpRunner();
        private readonly Timer _statusTimer = new Timer();

        // ---------- 服务端控件 ----------
        private TextBox _sBindAddr, _sBindPort, _sKcpPort, _sHttpPort, _sHttpsPort,
                        _sSubDomain, _sToken, _sDashPort, _sDashUser, _sDashPass;
        private ComboBox _sLogLevel;
        private TextBox _sLog;
        private Label _sStatusLabel, _sProcLabel;
        private Button _sBtnStart, _sBtnStop, _sBtnSvcInstall, _sBtnSvcRemove;

        // ---------- 客户端控件 ----------
        private TextBox _cAddr, _cPort, _cToken, _cAdminPort, _cAdminUser, _cAdminPass;
        private CheckBox _cTls, _cLoginFail;
        private ComboBox _cLogLevel;
        private ComboBox _cConnList;                  // 服务端连接下拉框
        private CheckBox _cConnEnabled;               // 启用当前连接
        private Button _cBtnAddConn, _cBtnDelConn, _cBtnRenameConn;
        private DataGridView _gridProxies, _gridVisitors;
        private BindingList<ProxyItem> _proxies;
        private BindingList<VisitorItem> _visitors;
        private TextBox _cLog;
        private Label _cStatusLabel, _cProcLabel;
        private Label _proxyCountLabel;               // 「共 N 条规则」
        private Button _visToggle;                    // ③ 访问者 展开/收起
        private TableLayoutPanel _visBox;             // ③ 访问者 所在的那一行
        private GroupBox _visGridHost;                // ③ 访问者 表格的外框（折叠时隐藏）
        private TableLayoutPanel _clientSettings;     // 客户端页的三段式布局表
        private bool _visitorsCollapsed = true;
        private Button _cBtnStart, _cBtnStop, _cBtnSvcInstall, _cBtnSvcRemove;

        /// <summary>正在切换连接时不要去回写界面，否则会把新连接的值覆盖掉。</summary>
        private bool _loadingConnections;

        /// <summary>「导入的服务器 N」用的递增序号。</summary>
        private int _importCounter;

        /// <summary>每个服务端连接对应一个 frpc 进程，用连接 Id 对上号。</summary>
        private readonly Dictionary<string, FrpRunner> _clientRunners = new Dictionary<string, FrpRunner>();

        // ---------- 其它 ----------
        private NotifyIcon _tray;
        private CheckBox _chkTray;
        private bool _reallyExit;

        private static readonly string[] LogLevels = { "trace", "debug", "info", "warn", "error" };
        private static readonly string[] ProxyTypes = { "tcp", "udp", "http", "https", "stcp", "xtcp" };
        private static readonly string[] VisitorTypes = { "stcp", "xtcp" };

        public MainForm()
        {
            _settings = ConfigStore.Load();

            SuspendLayout();
            Text = "FrpWin 内网穿透管理器  v1.0  (frp 0.71.0 · Windows x64)";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 700);
            ClientSize = new Size(1180, 1080);
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }
            try { Icon = Icon.ExtractAssociatedIcon(AppPaths.SelfExe); } catch { }

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8) };
            tabs.TabPages.Add(BuildServerTab());
            tabs.TabPages.Add(BuildClientTab());
            tabs.TabPages.Add(BuildAboutTab());
            Controls.Add(tabs);

            var status = new StatusStrip();
            var spring = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _chkTray = new CheckBox { Text = "关闭窗口时最小化到托盘", AutoSize = true };
            var host = new ToolStripControlHost(_chkTray) { AutoSize = true };
            status.Items.Add(spring);
            status.Items.Add(host);
            Controls.Add(status);

            _statusLabelCommon = spring;

            ResumeLayout(true);

            BuildTray();
            LoadSettingsIntoUi();
            HookRunnerLogs();

            _statusTimer.Interval = 2500;
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();

            FormClosing += OnFormClosing;

            // 控件都建好之后一次性把静态文字换成当前语言；
            // 动态文字（状态栏、日志）不在这里，它们各自走 Loc.F。
            Loc.Apply(this);
            if (_tray != null) Loc.Apply(_tray.ContextMenuStrip);

            RefreshStatus();

            AppendLog(_sLog, Loc.T("FrpWin 已就绪。"));
            AppendLog(_sLog, Loc.F("程序目录: {0}", AppPaths.InstallDir));
            AppendLog(_sLog, Loc.F("数据目录: {0}  (配置文件与日志都在这里)", AppPaths.DataDir));
            if (ServiceManager.IsAdministrator)
                AppendLog(_sLog, "当前已具有管理员权限，可以直接安装/卸载 Windows 服务。");
            else
                AppendLog(_sLog, "当前为普通用户权限；安装 Windows 服务时会提示以管理员身份重启。");
        }
        private ToolStripStatusLabel _statusLabelCommon;

        // =====================================================================
        //  服务端标签页
        // =====================================================================
        private TabPage BuildServerTab()
        {
            var page = new TabPage("服务端（frps · 部署在有公网的服务器）") { BackColor = SystemColors.Control, Padding = new Padding(8) };

            _sBindAddr = new TextBox();
            _sBindPort = new TextBox();
            _sKcpPort = new TextBox();
            _sHttpPort = new TextBox();
            _sHttpsPort = new TextBox();
            _sSubDomain = new TextBox();
            _sToken = new TextBox();
            _sDashPort = new TextBox();
            _sDashUser = new TextBox();
            _sDashPass = new TextBox();
            _sLogLevel = MakeCombo(LogLevels);
            _sLog = MakeLogBox();

            var form = new FormTable();
            form.Add("监听地址 bindAddr", _sBindAddr);
            form.Add("监听端口 bindPort", _sBindPort);
            form.Add("KCP 端口 kcpBindPort", _sKcpPort);
            form.Add("HTTP 虚拟主机端口", _sHttpPort);
            form.Add("HTTPS 虚拟主机端口", _sHttpsPort);
            form.Add("泛域名 subDomainHost", _sSubDomain);
            form.Add("认证令牌 token", _sToken);
            form.Add("Dashboard 端口", _sDashPort);
            form.Add("Dashboard 用户名", _sDashUser);
            form.Add("Dashboard 密码", _sDashPass);
            form.Add("日志级别 log.level", _sLogLevel);

            var hint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(760, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 2, 3, 8),
                Text = Loc.T("说明：端口填 0 表示关闭该功能。token 必须与客户端完全一致；") +
                       Loc.T("Dashboard 端口大于 0 时可用浏览器打开 http://服务器IP:端口 查看实时连接状态。")
            };
            form.AddSpan(hint);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 4, 10, 8), WrapContents = true };
            _sBtnStart = MakeButton("保存并启动服务端", 150, true);
            _sBtnStop = MakeButton("停止服务端", 100, false);
            _sBtnSvcInstall = MakeButton("安装为 Windows 服务", 155, false);
            _sBtnSvcRemove = MakeButton("卸载 Windows 服务", 155, false);
            var sBtnOpenCfg = MakeButton("查看配置文件", 110, false);
            var sBtnOpenDir = MakeButton("打开数据目录", 110, false);
            var sBtnFirewall = MakeButton("放行防火墙端口", 130, false);
            var sBtnClientFw = MakeButton("一键放行客户端端口", 170, false);
            _sBtnStart.Click += (s, e) => StartServer();
            _sBtnStop.Click += (s, e) => StopServer();
            _sBtnSvcInstall.Click += (s, e) => InstallService("frps", _sLog);
            _sBtnSvcRemove.Click += (s, e) => RemoveService("frps", _sLog);
            sBtnOpenCfg.Click += (s, e) => OpenFile(AppPaths.FrpsConfig);
            sBtnOpenDir.Click += (s, e) => OpenFolder(AppPaths.DataDir);
            sBtnFirewall.Click += (s, e) => OpenFirewallPorts();
            sBtnClientFw.Click += (s, e) => OpenClientFirewallPorts();

            var tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
            tips.SetToolTip(sBtnFirewall, "放行服务端自己的端口：bindPort、KCP、HTTP/HTTPS 虚拟主机、Dashboard（TCP + UDP，进站）。");
            tips.SetToolTip(sBtnClientFw, "读取 frps 的 Dashboard，列出所有已连接的客户端；\r\n选中某个客户端后勾选它占用的端口，一键在 Windows 防火墙里放行（可选进站 / 出站 / 都要）。");

            buttons.Controls.AddRange(new Control[] {
                _sBtnStart, _sBtnStop, _sBtnSvcInstall, _sBtnSvcRemove,
                sBtnOpenCfg, sBtnOpenDir, sBtnFirewall, sBtnClientFw });

            _sStatusLabel = MakeStatusLabel();
            _sProcLabel = MakeStatusLabel();
            var info = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 0, 10, 0), WrapContents = true };
            info.Controls.Add(_sStatusLabel);
            info.Controls.Add(_sProcLabel);

            page.Controls.Add(BuildPageLayout(form.Panel, buttons, info, _sLog));
            return page;
        }

        // =====================================================================
        //  关于 / 帮助标签页
        // =====================================================================
        private TabPage BuildAboutTab()
        {
            var page = new TabPage("使用说明 / 关于") { BackColor = SystemColors.Control, Padding = new Padding(8) };

            var box = new TextBox
            {
                Name = "aboutBox",
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                Text = AboutText()
            };

            // ---------------- 界面语言 ----------------
            _langCombo = new ComboBox
            {
                Name = "langCombo",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 190,
                Margin = new Padding(3, 6, 3, 3)
            };
            _langCombo.Items.Add(Loc.T("跟随安装程序"));
            _langCombo.Items.Add("中文");
            _langCombo.Items.Add("English");
            _langCombo.SelectedIndex = string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase) ? 2
                                     : (string.Equals(_settings.Language, "zh", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

            var applyLang = MakeButton("保存语言", 120, false);
            applyLang.Name = "btnApplyLang";
            applyLang.Click += (s, e) => ApplyLanguageChoice();

            var langRow = new FlowLayoutPanel
            {
                Name = "langRow",
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(4)
            };
            langRow.Controls.Add(new Label { Text = "界面语言", AutoSize = true, Margin = new Padding(3, 9, 8, 3) });
            langRow.Controls.Add(_langCombo);
            langRow.Controls.Add(applyLang);
            langRow.Controls.Add(new Label
            {
                Name = "langHint",
                Text = "改完语言后重启 FrpWin 才会全部生效。",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(12, 9, 3, 3)
            });

            var root = new TableLayoutPanel { Name = "aboutRoot", Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowCount = 2;
            root.Controls.Add(box, 0, 0);
            root.Controls.Add(langRow, 0, 1);

            page.Controls.Add(root);
            return page;
        }

        private ComboBox _langCombo;

        /// <summary>保存界面语言选择。语言要在下次启动时才全部生效，所以这里只存不重启。</summary>
        private void ApplyLanguageChoice()
        {
            if (_langCombo == null) return;

            string want = _langCombo.SelectedIndex == 2 ? "en"
                        : (_langCombo.SelectedIndex == 1 ? "zh" : Loc.Auto);

            if (string.Equals(want, _settings.Language ?? Loc.Auto, StringComparison.OrdinalIgnoreCase))
            {
                Info("界面语言没有变化。");
                return;
            }

            _settings.Language = want;
            try
            {
                ConfigStore.Save(_settings);
            }
            catch (Exception ex)
            {
                Warn(Loc.F("保存设置失败：{0}", ex.Message));
                return;
            }

            AppendLog(_sLog, Loc.F("界面语言已改为 {0}，重启后生效。", want));
            Info("界面语言已保存。\r\n\r\n关闭并重新打开 FrpWin 之后，整个界面就会切换过去。");
        }

        internal static string AboutText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.T("FrpWin —— Windows 专用 frp 内网穿透套装"));
            sb.AppendLine("======================================================");
            sb.AppendLine();
            sb.AppendLine(Loc.T("【包含内容】"));
            sb.AppendLine(Loc.T("  frps.exe    服务端程序，运行在具有公网 IP 的机器上（frp 官方 v0.71.0 源码编译，x64）"));
            sb.AppendLine(Loc.T("  frpc.exe    客户端程序，运行在需要被访问的内网机器上"));
            sb.AppendLine(Loc.T("  FrpWin.exe  本图形管理器，用于配置、启停、安装 Windows 服务"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("【五分钟上手】"));
            sb.AppendLine(Loc.T("  第一步（服务器，有公网 IP 的机器）："));
            sb.AppendLine(Loc.T("    1. 打开 FrpWin，切到“服务端”标签页；"));
            sb.AppendLine(Loc.T("    2. 确认监听端口（默认 7000）、设置一个 token（认证令牌）；"));
            sb.AppendLine(Loc.T("    3. 点“保存并启动服务端”；"));
            sb.AppendLine(Loc.T("    4. 点“放行防火墙端口”，把 7000 端口在防火墙中放行；"));
            sb.AppendLine(Loc.T("    5. 如需长期运行，点“安装为 Windows 服务”，开机自动启动。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("  第二步（内网机器）："));
            sb.AppendLine(Loc.T("    1. 打开 FrpWin，切到“客户端”标签页；"));
            sb.AppendLine(Loc.T("    2. 填写服务器公网 IP 和端口，token 要与服务端完全一致；"));
            sb.AppendLine(Loc.T("    3. 在下方代理表格里添加一条，例如："));
            sb.AppendLine(Loc.T("         名称 = 远程桌面   类型 = tcp   本地IP = 127.0.0.1"));
            sb.AppendLine(Loc.T("         本地端口 = 3389   远程端口 = 13389"));
            sb.AppendLine(Loc.T("    4. 想同时连多个服务端：点“＋ 新添服务器”，给每个服务端填自己的"));
        sb.AppendLine(Loc.T("       地址和 token、配自己的代理规则，勾上“启用”即可；日志会带连接名前缀；"));
        sb.AppendLine(Loc.T("    5. 点“保存并启动客户端”。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("  第三步（回到服务器，放行客户端要用的端口）："));
            sb.AppendLine(Loc.T("    1. 确认“Dashboard 端口”大于 0（默认 7500）并已保存启动；"));
            sb.AppendLine(Loc.T("    2. 点“一键放行客户端端口”，窗口里会列出所有已连接的客户端；"));
            sb.AppendLine(Loc.T("    3. 选中客户端 →“下一步：选择端口” → 勾选端口（可全选）→"));
            sb.AppendLine(Loc.T("       选择“进站 / 出站 / 都要” →“一键开启防火墙端口”。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("  第四步：在任何地方用“远程桌面”连接 服务器公网IP:13389 即可。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("【代理类型说明】"));
            sb.AppendLine(Loc.T("  tcp      通用 TCP 转发（远程桌面、SSH、数据库、游戏联机等）"));
            sb.AppendLine(Loc.T("  udp      通用 UDP 转发（DNS、部分游戏）"));
            sb.AppendLine(Loc.T("  http     网站穿透，用域名访问，需要服务端配置 vhostHTTPPort"));
            sb.AppendLine(Loc.T("  https    加密网站穿透，需要服务端配置 vhostHTTPSPort"));
            sb.AppendLine(Loc.T("  stcp     安全点对点，不暴露公网端口，需要访客端配合（访客表格）"));
            sb.AppendLine(Loc.T("  xtcp     点对点直连，速度快，NAT 类型合适时流量不经服务器中转"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("【端口与安全建议】"));
            sb.AppendLine(Loc.T("  · 一定要设置足够复杂的 token，否则任何人都能连上你的服务端；"));
            sb.AppendLine(Loc.T("  · frps 的 7000 端口务必在防火墙里只对可信来源开放；"));
            sb.AppendLine(Loc.T("  · Dashboard 的默认密码 admin 请务必修改。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("【数据位置】"));
            sb.AppendLine("  " + AppPaths.DataDir);
            sb.AppendLine(Loc.T("  其中 frps.toml / frpc.toml / frpc-2.toml … 是自动生成的 frp 配置文件"));
        sb.AppendLine(Loc.T("  （frpc.toml 是第 1 个服务端连接，frpc-2.toml 是第 2 个，以此类推），"));
        sb.AppendLine(Loc.T("  language.txt 记录安装时选的界面语言，"));
            sb.AppendLine(Loc.T("  logs 目录下是运行日志，ui-settings.xml 保存本界面的设置。"));
            sb.AppendLine();
            sb.AppendLine(Loc.T("【开源协议】"));
            sb.AppendLine(Loc.T("  frp 由 fatedier 开发，遵循 Apache-2.0 协议，项目地址 https://github.com/fatedier/frp"));
            sb.AppendLine(Loc.T("  本管理器为在其之上编写的 Windows 图形外壳，同样遵循 Apache-2.0 协议。"));
            return sb.ToString();
        }

        // =====================================================================
        //  布局辅助
        // =====================================================================
        private sealed class FormTable
        {
            private readonly TableLayoutPanel _t;
            public FormTable()
            {
                _t = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(12, 4, 12, 2)
                };
                _t.ColumnCount = 2;
                _t.ColumnStyles.Clear();
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235F));
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            }
            public TableLayoutPanel Panel { get { return _t; } }

            public void Add(string label, Control c)
            {
                _t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _t.RowCount = _t.RowStyles.Count;
                int r = _t.RowCount - 1;

                var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
                c.Anchor = AnchorStyles.Left;
                if (c is TextBox || c is ComboBox) c.Width = 280;

                _t.Controls.Add(l, 0, r);
                _t.Controls.Add(c, 1, r);
            }

            public void AddSpan(Control c)
            {
                _t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _t.RowCount = _t.RowStyles.Count;
                int r = _t.RowCount - 1;
                c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                _t.Controls.Add(c, 0, r);
                _t.SetColumnSpan(c, 2);
            }
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
        }

        /// <summary>
        /// 两列字段布局：一行放两组「标签 + 输入框」，用来把连接设置的高度压掉一半。
        /// </summary>
        private sealed class FormTable2
        {
            private readonly TableLayoutPanel _t;

            public FormTable2()
            {
                _t = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(12, 4, 12, 2)
                };
                _t.ColumnStyles.Clear();
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
                _t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                _t.ColumnCount = _t.ColumnStyles.Count;
            }

            public TableLayoutPanel Panel { get { return _t; } }

            public void Add(string l1, Control c1, string l2, Control c2)
            {
                _t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _t.RowCount = _t.RowStyles.Count;
                int r = _t.RowCount - 1;

                _t.Controls.Add(FieldLabel(l1), 0, r);
                Prepare(c1);
                _t.Controls.Add(c1, 1, r);

                _t.Controls.Add(FieldLabel(l2), 2, r);
                if (c2 != null)
                {
                    Prepare(c2);
                    _t.Controls.Add(c2, 3, r);
                }
            }

            public void AddSpan(Control c)
            {
                _t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _t.RowCount = _t.RowStyles.Count;
                int r = _t.RowCount - 1;
                c.Anchor = AnchorStyles.Left;
                _t.Controls.Add(c, 0, r);
                _t.SetColumnSpan(c, 4);
            }

            private static void Prepare(Control c)
            {
                c.Anchor = AnchorStyles.Left;
                if (c is TextBox || c is ComboBox) c.Width = 190;
            }
        }

        /// <summary>
        /// GroupBox 开了 AutoSize 之后，会把子控件的“首选高度”算大一圈，
        /// 白白多占几十个像素。这里按子控件真实布局出来的高度把外框收紧，
        /// 省下来的高度全部留给下面的表格。
        /// </summary>
        private static void TightenGroupBoxes(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var box = c as GroupBox;
                if (box != null && box.AutoSize)
                {
                    Control topChild = null;
                    foreach (Control ch in box.Controls)
                    {
                        if (ch.Dock == DockStyle.Top) { topChild = ch; break; }
                    }
                    if (topChild != null)
                    {
                        int chrome = box.Height - box.ClientSize.Height;   // 标题栏等外框高度
                        int want = topChild.Bottom + box.Padding.Bottom + chrome;
                        if (want > 0 && want < box.Height)
                        {
                            box.AutoSize = false;
                            box.Height = want;
                        }
                    }
                }
                if (c.HasChildren) TightenGroupBoxes(c);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TightenGroupBoxes(this);
        }

        private static Control BuildPageLayout(Control settingsContent, Control buttons, Control info, TextBox log)        {
            var scroll = new Panel { Name = "scrollArea", Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Control };
            scroll.Controls.Add(settingsContent);

            var top = new TableLayoutPanel { Name = "pageTop", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Controls.Add(scroll, 0, 0);
            top.Controls.Add(buttons, 0, 1);
            top.Controls.Add(info, 0, 2);

            var logBox = new GroupBox { Text = "运行日志（实时）", Dock = DockStyle.Fill, Padding = new Padding(6) };
            log.Dock = DockStyle.Fill;
            logBox.Controls.Add(log);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            // 日志框收窄一点，省出来的高度留给代理规则表格
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            root.Controls.Add(top, 0, 0);
            root.Controls.Add(logBox, 0, 1);
            return root;
        }

        private static TextBox MakeLogBox()
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.FromArgb(250, 250, 250),
                Font = new Font("Consolas", 8.5F),
                BorderStyle = BorderStyle.None
            };
        }

        private static ComboBox MakeCombo(string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
            c.Items.AddRange(items);
            c.SelectedIndex = 0;
            return c;
        }

        internal static Button MakeButton(string text, int minWidth, bool primary)
        {
            // 用 AutoSize 而不是固定宽度，否则不同 DPI / 字体下按钮文字会被截断
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(minWidth, 32),
                Padding = new Padding(8, 0, 8, 0),
                Margin = new Padding(3, 3, 6, 3),
                UseVisualStyleBackColor = true
            };
            if (primary) b.Font = new Font(b.Font, FontStyle.Bold);
            return b;
        }

        private static Label MakeStatusLabel()
        {
            return new Label { AutoSize = true, Margin = new Padding(3, 4, 18, 3), Text = "" };
        }

        // =====================================================================
        //  托盘
        // =====================================================================
        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon.ExtractAssociatedIcon(AppPaths.SelfExe); } catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "FrpWin 内网穿透管理器";
            _tray.Visible = false;

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主界面", null, (s, e) => RestoreFromTray());
            menu.Items.Add("打开数据目录", null, (s, e) => OpenFolder(AppPaths.DataDir));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) =>
            {
                _reallyExit = true;
                Close();
            });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
        }

        /// <summary>给界面文字自检用：托盘右键菜单。</summary>
        internal ContextMenuStrip TrayMenuForTest { get { return _tray == null ? null : _tray.ContextMenuStrip; } }

        /// <summary>给布局自检用：展开③访问者区域，以便断言展开后的布局也正常。</summary>
        internal void SetVisitorsExpandedForTest()
        {
            ApplyVisitorCollapsed(false);
            PerformLayout();
            foreach (Control c in Controls) c.PerformLayout();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            _tray.Visible = false;
        }

        /// <summary>第二个实例启动时调用，把已经运行的窗口拉到前台。</summary>
        public void ActivateFromOtherInstance()
        {
            try
            {
                RestoreFromTray();
                BringToFront();
                TopMost = true;
                TopMost = false;
                Activate();
            }
            catch { }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyExit && _chkTray.Checked && (_serverRunner.IsRunning || RunningClientCount() > 0))
            {
                e.Cancel = true;
                Hide();
                _tray.Visible = true;
                _tray.ShowBalloonTip(2000, Loc.T("FrpWin 仍在后台运行"),
                    Loc.T("frp 进程继续运行中。双击托盘图标可重新打开窗口。"), ToolTipIcon.Info);
                return;
            }

            _statusTimer.Stop();
            _serverRunner.Dispose();
            foreach (var kv in _clientRunners) { try { kv.Value.Dispose(); } catch { } }
            _clientRunners.Clear();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }

        // =====================================================================
        //  通用动作
        // =====================================================================
        private void HookRunnerLogs()
        {
            _serverRunner.Line += line => Post(_sLog, line);
        }

        /// <summary>取（必要时新建）某个服务端连接对应的 frpc 进程管理器。</summary>
        private FrpRunner RunnerFor(ClientConnection conn)
        {
            if (conn == null) return null;
            conn.EnsureValid();

            FrpRunner runner;
            if (_clientRunners.TryGetValue(conn.Id, out runner)) return runner;

            runner = new FrpRunner();
            string label = conn.Label;
            runner.Line += line => Post(_cLog, "[" + label + "] " + line);
            _clientRunners[conn.Id] = runner;
            return runner;
        }

        /// <summary>已经启动的客户端连接个数。</summary>
        private int RunningClientCount()
        {
            int n = 0;
            foreach (var kv in _clientRunners)
            {
                try { if (kv.Value.IsRunning) n++; } catch { }
            }
            return n;
        }

        private void StopAllClientRunners()
        {
            foreach (var kv in _clientRunners)
            {
                try { kv.Value.Stop(); } catch { }
            }
        }

        private void Post(TextBox box, string line)
        {
            if (box == null || box.IsDisposed) return;
            try
            {
                if (box.InvokeRequired) box.BeginInvoke(new Action<TextBox, string>(Post), box, line);
                else AppendLog(box, line);
            }
            catch { }
        }

        internal static void AppendLog(TextBox box, string line)
        {
            if (box == null) return;
            try
            {
                if (box.Lines.Length > 1200)
                {
                    var keep = new string[600];
                    Array.Copy(box.Lines, box.Lines.Length - 600, keep, 0, 600);
                    box.Lines = keep;
                }
                box.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + Loc.Whole(line) + Environment.NewLine);
                box.SelectionStart = box.TextLength;
                box.ScrollToCaret();
            }
            catch { }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Warn(Loc.F("无法打开目录：{0}", ex.Message)); }
        }

        private static void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path)) { Warn(Loc.F("文件还不存在，请先保存配置：\r\n{0}", path)); return; }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Warn(Loc.F("无法打开文件：{0}", ex.Message)); }
        }

        private void OpenFirewallPorts()
        {
            var ports = new List<int>();
            ports.Add(ParsePortOr(_sBindPort.Text, 7000));
            int kcp = ParsePortOr(_sKcpPort.Text, 0); if (kcp > 0) ports.Add(kcp);
            int http = ParsePortOr(_sHttpPort.Text, 0); if (http > 0) ports.Add(http);
            int https = ParsePortOr(_sHttpsPort.Text, 0); if (https > 0) ports.Add(https);
            int dash = ParsePortOr(_sDashPort.Text, 0); if (dash > 0) ports.Add(dash);

            var rules = new List<string>();
            foreach (var p in ports)
            {
                rules.Add("advfirewall firewall add rule name=\"FrpWin TCP " + p + "\" dir=in action=allow protocol=TCP localport=" + p);
            }
            foreach (var p in ports)
            {
                rules.Add("advfirewall firewall add rule name=\"FrpWin UDP " + p + "\" dir=in action=allow protocol=UDP localport=" + p);
            }

            if (!EnsureAdmin()) return;

            int ok = 0, fail = 0;
            foreach (var rule in rules)
            {
                try
                {
                    var psi = new ProcessStartInfo("netsh.exe", rule)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit(15000);
                        if (p.ExitCode == 0) ok++; else fail++;
                    }
                }
                catch { fail++; }
            }

            string portList = string.Join(", ", ports.ConvertAll(x => x.ToString()).ToArray());
            AppendLog(_sLog, Loc.F("防火墙规则处理完成：成功 {0} 条，失败 {1} 条。端口：{2}", ok, fail, portList));
            Info(Loc.F("已在 Windows 防火墙中放行以下端口：\r\n\r\n{0}\r\n\r\n成功 {1} 条规则{2}。",
                 portList, ok,
                 fail > 0 ? Loc.F("，失败 {0} 条（可能已存在同名规则）", fail) : ""));
        }

        /// <summary>
        /// “一键放行客户端端口”：
        /// 通过本机 frps 的 Dashboard 管理接口，列出所有已连接的客户端，
        /// 选中客户端 → 勾选它在服务端占用的端口 → 选进站/出站 → 一键写防火墙规则。
        /// </summary>
        private void OpenClientFirewallPorts()
        {
            int dashPort = ParsePortOr(_sDashPort.Text, 0);
            if (dashPort <= 0)
            {
                Warn(Loc.T("要列出“哪些客户端连上来了”，FrpWin 需要读取 frps 的 Dashboard 管理接口。\r\n\r\n") +
                     Loc.T("请先在“Dashboard 端口”里填一个大于 0 的端口（例如 7500），\r\n") +
                     Loc.T("然后点“保存并启动服务端”，再回来点这个按钮。"));
                _sDashPort.Focus();
                _sDashPort.SelectAll();
                return;
            }

            AppendLog(_sLog, Loc.F("打开“一键放行客户端端口”，Dashboard = http://127.0.0.1:{0}", dashPort));

            ClientPortWizard.Show(
                this,
                "127.0.0.1",
                dashPort,
                (_sDashUser.Text ?? "").Trim(),
                _sDashPass.Text ?? "",
                line => AppendLog(_sLog, line),
                EnsureAdmin);
        }

        private bool EnsureAdmin()
        {
            if (ServiceManager.IsAdministrator) return true;

            var r = MessageBox.Show(
                "该操作需要管理员权限。\r\n\r\n是否以管理员身份重新启动 FrpWin？\r\n（重启后请重新执行刚才的操作）",
                "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(AppPaths.SelfExe) { UseShellExecute = true, Verb = "runas" });
                    _reallyExit = true;
                    Close();
                }
                catch { Warn("无法请求管理员权限，请手工右键 FrpWin.exe 选择“以管理员身份运行”。"); }
            }
            return false;
        }

        private void InstallService(string role, TextBox log)
        {
            if (!EnsureAdmin()) return;
            if (!SaveAll(role)) return;

            string msg;
            bool ok;
            msg = ServiceManager.Install(role, out ok);
            AppendLog(log, msg);
            if (!ok) Warn(msg); else Info(msg);
            RefreshStatus();
        }

        private void RemoveService(string role, TextBox log)
        {
            if (!EnsureAdmin()) return;

            string msg;
            bool ok;
            msg = ServiceManager.Uninstall(role, out ok);
            AppendLog(log, msg);
            if (!ok) Warn(msg); else Info(msg);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            string srvName = ServiceManager.ServerServiceName;
            string cliName = ServiceManager.ClientServiceName;

            string srvSvc = ServiceManager.QueryState(srvName);
            string cliSvc = ServiceManager.QueryState(cliName);

            bool srvRun = _serverRunner.IsRunning;
            int cliTotal = 0, cliRun = 0;
            var pids = new List<int>();
            foreach (ClientConnection c in _settings.Client.Connections)
            {
                if (!c.Enabled) continue;
                cliTotal++;
                FrpRunner r;
                if (_clientRunners.TryGetValue(c.Id, out r) && r.IsRunning)
                {
                    cliRun++;
                    pids.Add(r.ProcessId);
                }
            }

            _sStatusLabel.Text = Loc.F("服务状态：{0}   |   界面进程：{1}", srvSvc,
                srvRun ? Loc.F("运行中 (PID {0})", _serverRunner.ProcessId) : Loc.T("未运行"));
            _sStatusLabel.ForeColor = srvRun || srvSvc == "RUNNING" ? Color.FromArgb(0, 120, 0) : Color.DimGray;

            string cliProc = cliRun == 0
                ? Loc.T("未运行")
                : Loc.F("运行中 {0}/{1} 个连接 (PID {2})", cliRun, cliTotal, string.Join(", ", pids.ConvertAll(x => x.ToString()).ToArray()));
            _cStatusLabel.Text = Loc.F("服务状态：{0}   |   界面进程：{1}", cliSvc, cliProc);
            _cStatusLabel.ForeColor = cliRun > 0 || cliSvc == "RUNNING" ? Color.FromArgb(0, 120, 0) : Color.DimGray;

            _sBtnStart.Enabled = !srvRun;
            _sBtnStop.Enabled = srvRun;

            bool clientBusy = RunningClientCount() > 0;
            _cBtnStart.Enabled = !(cliTotal > 0 && cliRun >= cliTotal);
            _cBtnStop.Enabled = clientBusy;

            if (_statusLabelCommon != null)
            {
                _statusLabelCommon.Text = Loc.F("frps 服务：{0}    frpc 服务：{1}    数据目录：{2}",
                    srvSvc, cliSvc, AppPaths.DataDir);
            }

            _sProcLabel.Text = File.Exists(AppPaths.FrpsExe) ? "" : Loc.T("⚠ 未找到 frps.exe，请重新安装。");
            _cProcLabel.Text = File.Exists(AppPaths.FrpcExe) ? "" : Loc.T("⚠ 未找到 frpc.exe，请重新安装。");
            _sProcLabel.ForeColor = Color.Firebrick;
            _cProcLabel.ForeColor = Color.Firebrick;
        }

        // =====================================================================
        //  校验辅助
        // =====================================================================
        private static int ParsePortOr(string text, int fallback)
        {
            int v;
            return int.TryParse((text ?? "").Trim(), out v) ? v : fallback;
        }

        private static bool ReadPort(TextBox box, string name, int min, int max, out int value)
        {
            value = 0;
            string raw = (box.Text ?? "").Trim();
            if (!int.TryParse(raw, out value))
            {
                Warn(Loc.F("{0} 必须是数字，当前填写的是：“{1}”", name, raw));
                box.Focus();
                box.SelectAll();
                return false;
            }
            if (value < min || value > max)
            {
                Warn(Loc.F("{0} 必须在 {1} ~ {2} 之间，当前为 {3}。", name, min, max, value));
                box.Focus();
                box.SelectAll();
                return false;
            }
            return true;
        }

        internal static void Warn(string msg)
        {
            MessageBox.Show(Loc.Whole(msg), "FrpWin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        internal static void Info(string msg)
        {
            MessageBox.Show(Loc.Whole(msg), "FrpWin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // =====================================================================
        //  界面自检：把所有控件的实际布局尺寸打印出来，
        //  供自动化测试断言“按钮文字没被截断”“按钮条没盖住表格”。
        // =====================================================================
        public string DumpLayoutReport()
        {
            return DumpLayoutOf(this);
        }

        /// <summary>自检用：报告③访问者折叠状态与②③两行的高度。</summary>
        internal string DumpVisitorState()
        {
            var sb = new StringBuilder();
            sb.Append("VS|collapsed=").Append(_visitorsCollapsed)
              .Append("|visGridHostVisible=").Append(_visGridHostVisible);
            if (_clientSettings != null)
            {
                sb.Append("|rows=").Append(_clientSettings.RowStyles.Count);
                foreach (RowStyle r in _clientSettings.RowStyles)
                    sb.Append("|").Append(r.SizeType).Append(":").Append(r.Height);
            }
            if (_visBox != null)
            {
                sb.Append("|visRows=").Append(_visBox.RowStyles.Count);
                foreach (RowStyle r in _visBox.RowStyles)
                    sb.Append("|").Append(r.SizeType).Append(":").Append(r.Height);
            }
            return sb.ToString();
        }

        /// <summary>把任意一个窗口/容器的控件实际布局打印出来（主窗口和子对话框共用）。</summary>
        internal static string DumpLayoutOf(Control root)
        {
            var form = root as MainForm;
            _dumpIncludeVisitors = form == null || form._visGridHostVisible;

            var sb = new StringBuilder();
            sb.AppendLine("FORM|" + root.ClientSize.Width + "|" + root.ClientSize.Height);
            DumpControls(root, root, sb);
            return sb.ToString();
        }

        /// <summary>③访问者收起时，它的子树不参与布局断言，否则残留尺寸会把结论带偏。</summary>
        private static bool _dumpIncludeVisitors = true;

        private bool _visGridHostVisible = true;

        private static bool IsInSubtree(Control c, string name)
        {
            for (Control p = c; p != null; p = p.Parent)
                if (string.Equals(p.Name, name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>供自检使用：切换到指定标签页。</summary>
        public void SelectTabForTest(int index)
        {
            foreach (Control c in Controls)
            {
                var tabs = c as TabControl;
                if (tabs != null && index >= 0 && index < tabs.TabPages.Count)
                {
                    tabs.SelectedIndex = index;
                    return;
                }
            }
        }

        internal static void DumpControls(Control root, Control parent, StringBuilder sb)
        {
            foreach (Control c in parent.Controls)
            {
                Rectangle r = root.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                string text = (c.Text ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
                string name = string.IsNullOrEmpty(c.Name) ? "-" : c.Name;

                // 被折叠隐藏的控件（比如收起来的③访问者）不参与布局断言，
                // 否则它们残留的旧尺寸会把测试结论带偏。
                // 注意：不能直接用 c.Visible —— TabControl 里没被选中的页
                // 会连带整页子控件的 Visible 都变成 false。
                if (!_dumpIncludeVisitors && IsInSubtree(c, "visGridHost")) continue;

                if (c is Button)
                {
                    // 按钮文字需要的宽度 vs 实际宽度，判断有没有被截断
                    int need = TextRenderer.MeasureText(text, c.Font).Width + c.Padding.Horizontal;
                    sb.AppendLine("BTN|" + name + "|" + text + "|" + r.Left + "|" + r.Top + "|" + r.Width + "|" + r.Height + "|" + need);
                }
                else if ((c is Label || c is CheckBox) && text.Length > 0)
                {
                    // 实际高度 > 单行高度 说明标签文字被挤成了两行
                    int single = TextRenderer.MeasureText("测", c.Font).Height + c.Padding.Vertical + 4;
                    sb.AppendLine("LBL|" + text + "|" + r.Width + "|" + r.Height + "|" + single);
                }
                else if (!string.IsNullOrEmpty(c.Name))
                {
                    // 字段：CTL|Type|Name|Left|Top|Width|Height
                    sb.AppendLine("CTL|" + c.GetType().Name + "|" + name + "|" + r.Left + "|" + r.Top + "|" + r.Width + "|" + r.Height);
                }

                if (c is DataGridView)
                {
                    // 表格：表头高度 / 行高 是否够放下当前字体的文字（防止文字被纵向截断）
                    var gv = (DataGridView)c;
                    sb.AppendLine("GRD|" + name
                        + "|" + gv.ColumnHeadersHeight
                        + "|" + gv.RowTemplate.Height
                        + "|" + LineHeight(gv.Font)
                        + "|" + gv.Font.Name + " " + gv.Font.SizeInPoints);
                }

                if (c.HasChildren) DumpControls(root, c, sb);
            }
        }
    }
}
