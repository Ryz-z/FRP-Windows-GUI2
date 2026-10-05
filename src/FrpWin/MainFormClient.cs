using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FrpWin
{
    public sealed partial class MainForm
    {
        // =====================================================================
        //  客户端标签页
        // =====================================================================
        private TabPage BuildClientTab()
        {
            var page = new TabPage("客户端（frpc · 部署在内网机器上）") { BackColor = SystemColors.Control, Padding = new Padding(8) };

            _cAddr = new TextBox();
            _cPort = new TextBox();
            _cToken = new TextBox();
            _cTls = new CheckBox { Name = "chkTls", Text = "启用 TLS 加密（推荐）", AutoSize = true };
            _cLoginFail = new CheckBox { Name = "chkLoginFail", Text = "登录失败即退出", AutoSize = true };
            _cLogLevel = MakeCombo(LogLevels);
            _cAdminPort = new TextBox();
            _cAdminUser = new TextBox();
            _cAdminPass = new TextBox();

            // 连接设置用两列布局，把纵向空间让给下面的两个表格
            _cLoginFail.Margin = new Padding(18, 3, 3, 3);
            var checks = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0)
            };
            checks.Controls.Add(_cTls);
            checks.Controls.Add(_cLoginFail);
            checks.Name = "checksPanel";

            var conn = new FormTable2();
            conn.Add("服务器地址", _cAddr, "服务器端口", _cPort);
            conn.Add("认证令牌", _cToken, "日志级别", _cLogLevel);
            // 两个勾选项放进「管理界面端口」右边的空位，省下一整行的纵向空间
            conn.Add("管理界面端口", _cAdminPort, "", checks);
            conn.Panel.Name = "connTable";

            // ---------------- 服务端连接选择条 ----------------
            // frpc 一个进程只能连一个服务端，所以「一个客户端连多个服务端」在这里
            // 表现为一组连接配置，每个连接各起一个 frpc 进程。
            _cConnList = new ComboBox { Name = "connList", DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _cConnEnabled = new CheckBox { Name = "connEnabled", Text = "启用", AutoSize = true, Margin = new Padding(16, 8, 6, 3) };

            _cBtnAddConn = MakeButton("＋ 新添服务器", 140, true);
            _cBtnAddConn.Name = "btnAddConn";
            _cBtnDelConn = MakeButton("－ 删除连接", 115, false);
            _cBtnDelConn.Name = "btnDelConn";
            _cBtnRenameConn = MakeButton("重命名", 90, false);
            _cBtnRenameConn.Name = "btnRenameConn";

            // 原来单独占一行，现在并进这条连接工具条里，省下一行的高度
            var advBtn = MakeButton("管理界面账号", 125, false);
            advBtn.Name = "btnAdminAccount";
            advBtn.Click += (s, e) => EditAdminAccount();

            _cBtnAddConn.Click += (s, e) => AddConnection();
            _cBtnDelConn.Click += (s, e) => RemoveConnection();
            _cBtnRenameConn.Click += (s, e) => RenameConnection();
            _cConnList.SelectedIndexChanged += (s, e) => OnConnectionChanged();
            _cConnEnabled.CheckedChanged += (s, e) =>
            {
                if (_loadingConnections) return;
                var cur = CurrentConnection();
                if (cur != null) cur.Enabled = _cConnEnabled.Checked;
            };

            var connBar = new FlowLayoutPanel
            {
                Name = "connBar",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 12, 0)
            };
            connBar.Controls.Add(new Label { Text = "服务端连接", AutoSize = true, Margin = new Padding(3, 8, 8, 3) });
            connBar.Controls.Add(_cConnList);
            connBar.Controls.Add(_cBtnAddConn);
            connBar.Controls.Add(_cBtnDelConn);
            connBar.Controls.Add(_cBtnRenameConn);
            connBar.Controls.Add(advBtn);
            var tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
            tips.SetToolTip(advBtn, "修改 frpc 自带本地管理界面的用户名和密码。");
            _cConnEnabled.Name = "connEnabled";
            tips.SetToolTip(_cConnEnabled, "勾上之后，点“保存并启动客户端”才会启动这个服务端连接。");
            connBar.Controls.Add(_cConnEnabled);

            var connInner = new TableLayoutPanel
            {
                Name = "connInner",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            connInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            connInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            connInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            connInner.RowCount = 2;
            connInner.Controls.Add(connBar, 0, 0);
            connInner.Controls.Add(conn.Panel, 0, 1);

            var connBox = new GroupBox
            {
                Text = "① 连接设置", Name = "connBox",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };
            connBox.Controls.Add(connInner);

            // ---------------- 代理表格 ----------------
            _gridProxies = MakeGrid();
            _gridProxies.Name = "gridProxies";
            _gridProxies.Columns.Add(MakeText("名称", "Name", 90));
            _gridProxies.Columns.Add(MakeTypeColumn("类型", "Type", ProxyTypes, 80));
            _gridProxies.Columns.Add(MakeText("本地IP", "LocalIP", 95));
            _gridProxies.Columns.Add(MakeText("本地端口", "LocalPort", 70));
            _gridProxies.Columns.Add(MakeText("远程端口", "RemotePort", 70));
            _gridProxies.Columns.Add(MakeText("自定义域名", "CustomDomains", 150));
            _gridProxies.Columns.Add(MakeText("子域名", "Subdomain", 80));
            _gridProxies.Columns.Add(MakeText("密钥", "SecretKey", 85));
            _gridProxies.Columns.Add(MakeCheck("加密", "UseEncryption", 45));
            _gridProxies.Columns.Add(MakeCheck("压缩", "UseCompression", 45));

            var proxyBar = MakeToolBar();
            proxyBar.Name = "proxyBar";
            var pAddTcp = MakeButton("＋ 添加 TCP/UDP 转发", 150, false);
            var pAddWeb = MakeButton("＋ 添加网站穿透", 120, false);
            var pAddP2P = MakeButton("＋ 添加点对点(stcp)", 140, false);
            var pImport = MakeButton("导入配置", 95, false);
            pImport.Name = "btnImportProxy";
            var pDel = MakeButton("－ 删除选中行", 100, false);
            var pCopy = MakeButton("复制选中行", 95, false);
            pAddTcp.Click += (s, e) => AddProxy("tcp");
            pAddWeb.Click += (s, e) => AddProxy("http");
            pAddP2P.Click += (s, e) => AddProxy("stcp");
            pImport.Click += (s, e) => ImportFrpcConfig();
            pDel.Click += (s, e) => RemoveSelected(_gridProxies);
            pCopy.Click += (s, e) => DuplicateSelected(_gridProxies);
            proxyBar.Controls.AddRange(new Control[] { pAddTcp, pAddWeb, pAddP2P, pImport, pDel, pCopy });

            var proxyBox = new GroupBox { Name = "proxyBox", Text = "② 代理规则（要穿透哪些服务）", Dock = DockStyle.Fill, Padding = new Padding(6) };
            proxyBox.Controls.Add(StackBarOverGrid(proxyBar, _gridProxies));

            // ---------------- 访问者表格 ----------------
            // 访问者只在 stcp / xtcp 点对点穿透时才用得上，所以整块做成可以折叠的：
            // 默认收起来，把纵向空间全留给上面的代理规则表格；需要时点一下展开。
            _gridVisitors = MakeGrid();
            _gridVisitors.Name = "gridVisitors";
            _gridVisitors.Columns.Add(MakeText("名称", "Name", 110));
            _gridVisitors.Columns.Add(MakeTypeColumn("类型", "Type", VisitorTypes, 70));
            _gridVisitors.Columns.Add(MakeText("对应代理名", "ServerName", 150));
            _gridVisitors.Columns.Add(MakeText("密钥", "SecretKey", 100));
            _gridVisitors.Columns.Add(MakeText("绑定地址", "BindAddr", 110));
            _gridVisitors.Columns.Add(MakeText("绑定端口", "BindPort", 100));

            var visBar = MakeToolBar();
            visBar.Name = "visBar";
            var vAdd = MakeButton("＋ 添加访问者", 110, false);
            var vDel = MakeButton("－ 删除选中行", 100, false);
            vAdd.Click += (s, e) => AddVisitor();
            vDel.Click += (s, e) => RemoveSelected(_gridVisitors);
            visBar.Controls.AddRange(new Control[] { vAdd, vDel });

            var visGridHost = new GroupBox
            {
                Name = "visGridHost",
                Text = "③ 访问者（仅 stcp / xtcp 点对点穿透时需要，访问方填写）",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            visGridHost.Controls.Add(StackBarOverGrid(visBar, _gridVisitors));

            var visBox = new TableLayoutPanel
            {
                Name = "visBox",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            visBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            visBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            visBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            visBox.Controls.Add(visGridHost, 0, 1);
            _visBox = visBox;
            _visGridHost = visGridHost;

            _visToggle = MakeButton("③ 访问者（点此展开）", 220, false);
            _visToggle.Name = "visToggle";
            _visToggle.Click += (s, e) => ToggleVisitors();
            var visHead = new FlowLayoutPanel
            {
                Name = "visHead",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
            };
            visHead.Controls.Add(_visToggle);
            visBox.Controls.Add(visHead, 0, 0);

            var settings = new TableLayoutPanel
            {
                Name = "clientSettings",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3
            };
            // ② 代理规则是这一页最常用的地方，所以高度基本都给它：
            //    默认一次能看到 8 行以上规则。访问者这块默认收起，展开后表格照样放得下表头 + 2 行。
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 448F));   // ② 代理规则
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 190F));   // ③ 访问者（展开时）
            settings.Controls.Add(connBox, 0, 0);
            settings.Controls.Add(proxyBox, 0, 1);
            settings.Controls.Add(visBox, 0, 2);

            _clientSettings = settings;
            ApplyVisitorCollapsed(true);

            var hint = new Label
            {
                Name = "proxyHint",
                AutoSize = true,
                MaximumSize = new Size(430, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(14, 9, 3, 3),
                Text = "提示：远程端口要在服务端放行。"
            };
            // 提示语放到滚动区下面的按钮条里（见下），这一行就不占高度了

            // ---------------- 按钮与日志 ----------------
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 4, 10, 8), WrapContents = true };
            _cBtnStart = MakeButton("保存并启动客户端", 150, true);
            _cBtnStop = MakeButton("停止客户端", 100, false);
            _cBtnSvcInstall = MakeButton("安装为 Windows 服务", 155, false);
            _cBtnSvcRemove = MakeButton("卸载 Windows 服务", 155, false);
            var cBtnOpenCfg = MakeButton("查看配置文件", 110, false);
            var cBtnOpenDir = MakeButton("打开数据目录", 110, false);
            _cBtnStart.Click += (s, e) => StartClient();
            _cBtnStop.Click += (s, e) => StopClient();
            _cBtnSvcInstall.Click += (s, e) => InstallService("frpc", _cLog);
            _cBtnSvcRemove.Click += (s, e) => RemoveService("frpc", _cLog);
            cBtnOpenCfg.Click += (s, e) =>
                OpenFile(AppPaths.FrpcConfigFor(_cConnList != null && _cConnList.SelectedIndex >= 0 ? _cConnList.SelectedIndex : 0));
            cBtnOpenDir.Click += (s, e) => OpenFolder(AppPaths.DataDir);
            buttons.Controls.AddRange(new Control[] {
                _cBtnStart, _cBtnStop, _cBtnSvcInstall, _cBtnSvcRemove, cBtnOpenCfg, cBtnOpenDir });


            _cStatusLabel = MakeStatusLabel();
            _cProcLabel = MakeStatusLabel();

            // 「共 N 条规则」放在这一行：工具条已经被六个按钮占满了，
            // 再塞一个标签会折行，把代理表格的高度吃掉。
            _proxyCountLabel = new Label
            {
                Name = "proxyCount",
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 4, 18, 3)
            };

            var info = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 0, 10, 0), WrapContents = true };
            info.Controls.Add(_cStatusLabel);
            info.Controls.Add(_proxyCountLabel);
            info.Controls.Add(_cProcLabel);
            info.Controls.Add(hint);

            _cLog = MakeLogBox();
            page.Controls.Add(BuildPageLayout(settings, buttons, info, _cLog));
            return page;
        }

        /// <summary>表格上方的按钮条。</summary>
        private static FlowLayoutPanel MakeToolBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 4)
            };
        }

        /// <summary>
        /// 把按钮条和表格上下摆好。
        /// 这里必须用 TableLayoutPanel 明确分行，不能靠 Dock + BringToFront：
        /// BringToFront 会把 Dock=Top 的按钮条在 z 序里提到最前面，导致停靠顺序反转，
        /// 表格会先占满整个区域，按钮条再盖在表格表头上，第一行就被压掉一半。
        /// </summary>
        private static TableLayoutPanel StackBarOverGrid(Control bar, DataGridView grid)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            // 注意：RowCount 赋值不会自动填充 RowStyles，必须用 Add 而不是索引器
            t.RowStyles.Clear();
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            t.RowCount = t.RowStyles.Count;

            grid.Dock = DockStyle.Fill;
            grid.Margin = new Padding(0);

            t.Controls.Add(bar, 0, 0);
            t.Controls.Add(grid, 0, 1);
            return t;
        }

        /// <summary>一行文字在当前字体下的实际像素高度。</summary>
        internal static int LineHeight(Font font)
        {
            return TextRenderer.MeasureText("测试Ag", font).Height;
        }

        private DataGridView MakeGrid()
        {
            // 关键：表头高度和行高都必须按“当前字体”算出来。
            // 之前表头用的是 DisableResizing + 默认高度，那个高度是按控件构造时的
            // 系统默认字体算的；等控件被加到窗体上、继承了 9pt 微软雅黑之后，
            // 字体变高了，冻结的表头高度却不会跟着变，于是表头文字被从下往上截掉一半。
            int line = LineHeight(this.Font);

            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = true,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                // AutoSize = 高度跟着字体自动算，同时用户依然不能拖动改变
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                // 列按权重铺满整个宽度，右侧不留空白；窗口太窄时自动出现横向滚动条
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = this.Font,
                RowTemplate = { Height = line + 9 }
            };
            g.DataError += (s, e) => { e.ThrowException = false; };

            // 字体将来若因 DPI/主题变化，行高同步跟着变
            g.FontChanged += (s, e) => { g.RowTemplate.Height = LineHeight(g.Font) + 9; };
            return g;
        }

        private static DataGridViewTextBoxColumn MakeText(string header, string prop, int weight)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                FillWeight = weight,
                MinimumWidth = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static DataGridViewCheckBoxColumn MakeCheck(string header, string prop, int weight)
        {
            return new DataGridViewCheckBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                FillWeight = weight,
                MinimumWidth = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static DataGridViewComboBoxColumn MakeTypeColumn(string header, string prop, string[] values, int weight)
        {
            var c = new DataGridViewComboBoxColumn
            {
                HeaderText = header,
                DataPropertyName = prop,
                FillWeight = weight,
                MinimumWidth = weight,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            c.Items.AddRange(values);
            return c;
        }

        private void AddProxy(string type)
        {
            if (_proxies == null) return;
            int n = 1;
            string name;
            do { name = "proxy" + n++; } while (ContainsProxyName(name));

            var p = new ProxyItem { Name = name, Type = type, LocalIP = "127.0.0.1", LocalPort = 80, RemotePort = NextFreeRemotePort() };

            if (type == "http" || type == "https") { p.CustomDomains = "www.example.com"; p.RemotePort = 0; }
            if (type == "stcp" || type == "xtcp") { p.SecretKey = ConfigStore.RandomToken(); p.RemotePort = 0; }

            _proxies.Add(p);
            SelectLast(_gridProxies);
        }

        private bool ContainsProxyName(string name)
        {
            foreach (var p in _proxies)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private int NextFreeRemotePort()
        {
            int port = 6000;
            var used = new HashSet<int>();
            foreach (var p in _proxies) used.Add(p.RemotePort);
            while (used.Contains(port) && port < 65000) port++;
            return port;
        }

        private void AddVisitor()
        {
            if (_visitors == null) return;
            int n = 1;
            string name;
            do { name = "visitor" + n++; } while (ContainsVisitorName(name));

            _visitors.Add(new VisitorItem { Name = name, Type = "stcp", SecretKey = ConfigStore.RandomToken(), BindPort = 6000 });
            SelectLast(_gridVisitors);
        }

        private bool ContainsVisitorName(string name)
        {
            foreach (var v in _visitors)
                if (string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void SelectLast(DataGridView g)
        {
            if (g.Rows.Count == 0) return;
            g.ClearSelection();
            int i = g.Rows.Count - 1;
            g.Rows[i].Selected = true;
            g.CurrentCell = g.Rows[i].Cells[0];
            g.BeginEdit(true);
        }

        /// <summary>
        /// 把 DataGridView 里正在编辑、还没提交的单元格内容落到绑定对象上，
        /// 否则用户刚改完就点“保存并启动”会保存成旧值。
        /// </summary>
        private static void CommitGridEdits(DataGridView g)
        {
            if (g == null) return;
            try
            {
                g.EndEdit();
                if (g.DataSource != null && g.BindingContext != null)
                {
                    var cm = g.BindingContext[g.DataSource] as CurrencyManager;
                    if (cm != null) cm.EndCurrentEdit();
                }
            }
            catch { }
        }

        private static void RemoveSelected(DataGridView g)
        {
            var list = g.DataSource as System.Collections.IList;
            if (list == null) return;

            // 先提交正在编辑的单元格，否则删除后旧内容会被回写
            g.EndEdit();

            var idx = new List<int>();
            foreach (DataGridViewCell cell in g.SelectedCells)
                if (!idx.Contains(cell.RowIndex)) idx.Add(cell.RowIndex);
            idx.Sort();
            idx.Reverse();

            foreach (int i in idx)
            {
                if (i >= 0 && i < list.Count) list.RemoveAt(i);
            }
        }

        private static void DuplicateSelected(DataGridView g)
        {
            var list = g.DataSource as System.Collections.IList;
            if (list == null) return;
            g.EndEdit();

            int i = g.CurrentCell != null ? g.CurrentCell.RowIndex : -1;
            if (i < 0 || i >= list.Count) return;

            var src = list[i];
            var clone = src is ProxyItem ? (object)((ProxyItem)src).Clone() : null;
            if (clone == null) return;

            var p = (ProxyItem)clone;
            p.Name = p.Name + "_copy";
            int n = 1;
            while (ContainsName(list, p.Name)) { p.Name = p.Name + n++; }
            list.Add(p);
        }

        private static bool ContainsName(System.Collections.IList list, string name)
        {
            foreach (var o in list)
            {
                var p = o as ProxyItem;
                if (p != null && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void EditAdminAccount()
        {
            using (var dlg = new AdminAccountDialog(_cAdminUser.Text, _cAdminPass.Text))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _cAdminUser.Text = dlg.UserName;
                    _cAdminPass.Text = dlg.Password;
                }
            }
        }

        // =====================================================================
        //  导入现成的 frpc 配置
        // =====================================================================

        /// <summary>同名条目自动让路，不覆盖用户已有规则。</summary>
        private static string UniqueName(string want, ICollection<string> used)
        {
            string baseName = string.IsNullOrWhiteSpace(want) ? "proxy" : want.Trim();
            string name = baseName;
            int n = 2;
            while (used.Contains(name)) name = baseName + "_" + (n++);
            return name;
        }

        /// <summary>改个名字再并进当前连接，避免重名导致 frpc 起不来。</summary>
        private void MergeImportedInto(ClientConnection target, FrpcConfigFile.ImportResult r)
        {
            var proxyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProxyItem p in target.Proxies)
                if (p != null && p.Name != null) proxyNames.Add(p.Name);

            foreach (ProxyItem p in r.Proxies)
            {
                ProxyItem copy = p.Clone();
                copy.Name = UniqueName(copy.Name, proxyNames);
                proxyNames.Add(copy.Name);
                target.Proxies.Add(copy);
            }

            var visitorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VisitorItem v in target.Visitors)
                if (v != null && v.Name != null) visitorNames.Add(v.Name);

            foreach (VisitorItem v in r.Visitors)
            {
                VisitorItem copy = v.Clone();
                copy.Name = UniqueName(copy.Name, visitorNames);
                visitorNames.Add(copy.Name);
                target.Visitors.Add(copy);
            }
        }

        /// <summary>
        /// 「导入配置」：把别的机器上的 frpc.toml 内容识别出来，
        /// 要么新添成一个服务端连接，要么并进当前连接。
        /// </summary>
        private void ImportFrpcConfig()
        {
            StoreCurrentConnectionFromUi();
            ClientConnection cur = CurrentConnection();
            string curLabel = cur == null ? "" : cur.Label;

            FrpcConfigFile.ImportResult r;
            bool asNew;
            using (var dlg = new FrpcImportForm(curLabel))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                r = dlg.Result;
                asNew = dlg.ImportAsNewConnection;
            }

            if (r == null || !r.HasAnything) return;

            if (asNew)
            {
                _importCounter++;
                string label = Loc.F("导入的服务器 {0}", _importCounter);
                int n = _importCounter;
                while (LabelExists(label))
                {
                    n++;
                    label = Loc.F("导入的服务器 {0}", n);
                }

                var c = new ClientConnection
                {
                    Label = label,
                    Enabled = true,
                    ServerAddr = r.HasServer ? r.ServerAddr : "",
                    ServerPort = r.HasServer && r.ServerPort > 0 ? r.ServerPort : 7000,
                    Token = r.Token,
                    TlsEnable = r.HasTls && r.TlsEnable,
                    LogLevel = string.IsNullOrWhiteSpace(r.LogLevel) ? "info" : r.LogLevel,
                    LoginFailExit = r.LoginFailExit
                };
                if (r.AdminPort > 0)
                {
                    c.AdminPort = r.AdminPort;
                    c.AdminUser = r.AdminUser;
                    c.AdminPassword = r.AdminPassword;
                }

                MergeImportedInto(c, r);

                // 校验一遍：服务器地址之类的必填项缺了就让用户先补上，别等到启动才报错
                int idx = _settings.Client.Connections.Count;
                if (!ValidateConnection(c, idx))
                {
                    AppendLog(_cLog, Loc.T("导入已中止：上面提示的必填项补齐后再试一次。"));
                    return;
                }

                _settings.Client.Connections.Add(c);
                _settings.Client.Selected = _settings.Client.Connections.Count - 1;
                RefreshConnectionList();
                LoadCurrentConnectionIntoUi();

                AppendLog(_cLog, Loc.F("已导入为新服务端连接「{0}」：{1} 条代理规则、{2} 条访问者。",
                    c.Label, r.Proxies.Count, r.Visitors.Count));
                if (!r.HasServer || string.IsNullOrWhiteSpace(r.ServerAddr))
                    AppendLog(_cLog, Loc.T("这份配置里没有服务器地址，请补上「服务器地址」再启动。"));
                _cAddr.Focus();
                return;
            }

            // 并进当前连接
            if (cur == null) return;

            if (r.HasServer && !string.IsNullOrWhiteSpace(r.ServerAddr))
            {
                cur.ServerAddr = r.ServerAddr;
                if (r.ServerPort > 0) cur.ServerPort = r.ServerPort;
            }
            if (r.HasToken) cur.Token = r.Token;
            if (r.HasTls) cur.TlsEnable = r.TlsEnable;
            if (!string.IsNullOrWhiteSpace(r.LogLevel)) cur.LogLevel = r.LogLevel;
            if (r.AdminPort > 0)
            {
                cur.AdminPort = r.AdminPort;
                cur.AdminUser = r.AdminUser;
                cur.AdminPassword = r.AdminPassword;
            }

            MergeImportedInto(cur, r);
            LoadCurrentConnectionIntoUi();
            AppendLog(_cLog, Loc.F("已把 {0} 条代理规则、{1} 条访问者并进连接「{2}」。",
                r.Proxies.Count, r.Visitors.Count, cur.Label));
            if (r.ExtraKeyCount > 0)
                AppendLog(_cLog, Loc.F("其中 {0} 个界面外的参数已原样保留，生成配置时会照写。", r.ExtraKeyCount));
        }

        /// <summary>工具条上的「共 N 条规则」，添加 / 删除 / 导入后同步刷新。</summary>
        private void UpdateRuleCount()
        {
            if (_proxyCountLabel == null) return;
            int p = _proxies == null ? 0 : _proxies.Count;
            int v = _visitors == null ? 0 : _visitors.Count;
            _proxyCountLabel.Text = Loc.F("共 {0} 条规则、{1} 条访问者", p, v);
        }

        // =====================================================================
        //  ③ 访问者：默认收起，把高度让给②代理规则
        // =====================================================================

        private void ToggleVisitors()
        {
            ApplyVisitorCollapsed(!_visitorsCollapsed);
        }

        /// <summary>
        /// 收起时把访问者表格藏起来，并把③那一行的高度压到只剩一个按钮，
        /// 省下来的高度全部归②代理规则表格（用 Panel 包一层，折叠后不占空位）。
        /// </summary>
        private void ApplyVisitorCollapsed(bool collapsed)
        {
            _visitorsCollapsed = collapsed;
            if (_visToggle != null)
                _visToggle.Text = collapsed ? "③ 访问者（点此展开）" : "③ 访问者（点此收起）";

            if (_visGridHost != null) _visGridHost.Visible = !collapsed;
            _visGridHostVisible = !collapsed;

            if (_visBox != null && _visBox.RowStyles.Count > 0)
                _visBox.RowStyles[0].Height = collapsed ? 34F : 30F;

            if (_clientSettings != null && _clientSettings.RowStyles.Count > 2)
                _clientSettings.RowStyles[2].Height = collapsed ? 44F : 190F;

            // 折叠 / 展开之后表格尺寸变了，让工具条上的计数重新排一下版
            if (_gridProxies != null) _gridProxies.Invalidate();

            // 改行高之后主动重排一次：TableLayoutPanel 改了 RowStyle 不会自动
            // 给里面的控件重新分配尺寸，从最外层排一遍最稳。
            var top = TopLevelControl;
            if (top != null) top.PerformLayout();
            else PerformLayout();
            Update();
        }

        private static Control FindControlByName(Control root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
            foreach (Control c in root.Controls)
            {
                Control found = FindControlByName(c, name);
                if (found != null) return found;
            }
            return null;
        }

        // =====================================================================
        //  载入 / 保存
        // =====================================================================
        private void LoadSettingsIntoUi()
        {
            var s = _settings.Server;
            _sBindAddr.Text = s.BindAddr;
            _sBindPort.Text = s.BindPort.ToString();
            _sKcpPort.Text = s.KcpBindPort.ToString();
            _sHttpPort.Text = s.VhostHttpPort.ToString();
            _sHttpsPort.Text = s.VhostHttpsPort.ToString();
            _sSubDomain.Text = s.SubDomainHost;
            _sToken.Text = s.Token;
            _sDashPort.Text = s.DashboardPort.ToString();
            _sDashUser.Text = s.DashboardUser;
            _sDashPass.Text = s.DashboardPassword;
            SelectLevel(_sLogLevel, s.LogLevel);

            var c = _settings.Client;

            _loadingConnections = true;
            try
            {
                RefreshConnectionList();
            }
            finally { _loadingConnections = false; }
            LoadCurrentConnectionIntoUi();

            _chkTray.Checked = true;
        }

        // ---------------------------------------------------------------------
        //  多个服务端连接：下拉框切换 / 新增 / 删除 / 重命名
        // ---------------------------------------------------------------------

        private ClientConnection CurrentConnection()
        {
            return _settings.Client.Current();
        }

        private bool LabelExists(string label)
        {
            foreach (var c in _settings.Client.Connections)
                if (string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>把连接列表刷进下拉框，文字里带上「已停用」标记。</summary>
        private void RefreshConnectionList()
        {
            bool wasLoading = _loadingConnections;
            _loadingConnections = true;
            try
            {
                _cConnList.Items.Clear();
                foreach (var c in _settings.Client.Connections)
                {
                    c.EnsureValid();
                    string text = string.IsNullOrEmpty(c.Label) ? c.Id.Substring(0, 6) : c.Label;
                    if (!c.Enabled) text += "（已停用）";
                    _cConnList.Items.Add(text);
                }

                int sel = _settings.Client.Selected;
                if (sel < 0 || sel >= _cConnList.Items.Count) sel = _cConnList.Items.Count > 0 ? 0 : -1;
                _cConnList.SelectedIndex = sel;
                _settings.Client.Selected = sel < 0 ? 0 : sel;
            }
            finally { _loadingConnections = wasLoading; }
        }

        /// <summary>把当前选中的连接读进输入框和两个表格。</summary>
        private void LoadCurrentConnectionIntoUi()
        {
            ClientConnection c = CurrentConnection();
            if (c == null) return;

            _loadingConnections = true;
            try
            {
                _cAddr.Text = c.ServerAddr;
                _cPort.Text = c.ServerPort.ToString();
                _cToken.Text = c.Token;
                _cTls.Checked = c.TlsEnable;
                _cLoginFail.Checked = c.LoginFailExit;
                SelectLevel(_cLogLevel, c.LogLevel);
                _cAdminPort.Text = c.AdminPort.ToString();
                _cAdminUser.Text = c.AdminUser;
                _cAdminPass.Text = c.AdminPassword;
                _cConnEnabled.Checked = c.Enabled;

                CommitGridEdits(_gridProxies);
                CommitGridEdits(_gridVisitors);

                _proxies = new BindingList<ProxyItem>(c.Proxies);
                _proxies.ListChanged += (s, e) => UpdateRuleCount();
                _gridProxies.DataSource = _proxies;
                _visitors = new BindingList<VisitorItem>(c.Visitors);
                _visitors.ListChanged += (s, e) => UpdateRuleCount();
                _gridVisitors.DataSource = _visitors;

                UpdateRuleCount();

                _cBtnDelConn.Enabled = _settings.Client.Connections.Count > 1;
            }
            finally { _loadingConnections = false; }
        }

        /// <summary>把输入框里正在编辑的内容写回当前连接（不校验）。</summary>
        private void StoreCurrentConnectionFromUi()
        {
            ClientConnection c = CurrentConnection();
            if (c == null) return;

            c.ServerAddr = (_cAddr.Text ?? "").Trim();
            c.Token = (_cToken.Text ?? "").Trim();
            c.TlsEnable = _cTls.Checked;
            c.LoginFailExit = _cLoginFail.Checked;
            c.LogLevel = Convert.ToString(_cLogLevel.SelectedItem);
            c.AdminUser = _cAdminUser.Text;
            c.AdminPassword = _cAdminPass.Text;

            int v;
            c.ServerPort = int.TryParse((_cPort.Text ?? "").Trim(), out v) ? v : -1;
            c.AdminPort = int.TryParse((_cAdminPort.Text ?? "").Trim(), out v) ? v : -1;

            CommitGridEdits(_gridProxies);
            CommitGridEdits(_gridVisitors);
        }

        private void OnConnectionChanged()
        {
            if (_loadingConnections) return;
            if (_cConnList.SelectedIndex < 0) return;

            StoreCurrentConnectionFromUi();
            _settings.Client.Selected = _cConnList.SelectedIndex;
            LoadCurrentConnectionIntoUi();
        }

        private void AddConnection()
        {
            StoreCurrentConnectionFromUi();

            int n = _settings.Client.Connections.Count + 1;
            string label;
            do { label = ConfigStore.DefaultLabel(n - 1); n++; } while (LabelExists(label));

            var c = new ClientConnection
            {
                Label = label,
                ServerAddr = "",
                ServerPort = 7000,
                // 多个服务端通常用同一个 token，直接沿用当前填的，省得重打
                Token = (_cToken.Text ?? "").Trim(),
                TlsEnable = _cTls.Checked,
                LogLevel = Convert.ToString(_cLogLevel.SelectedItem),
                Enabled = true
            };

            _settings.Client.Connections.Add(c);
            _settings.Client.Selected = _settings.Client.Connections.Count - 1;

            RefreshConnectionList();
            LoadCurrentConnectionIntoUi();
            AppendLog(_cLog, Loc.F("已新添服务端连接「{0}」，请填写它的服务器地址和端口。", c.Label));
            _cAddr.Focus();
        }

        private void RemoveConnection()
        {
            if (_settings.Client.Connections.Count <= 1)
            {
                Warn("至少要保留一个服务端连接。");
                return;
            }

            ClientConnection c = CurrentConnection();
            string ask = Loc.F("确定要删除服务端连接「{0}」吗？\r\n\r\n它下面的代理规则和访问者也会一起删掉。", c.Label);
            if (MessageBox.Show(ask, "FrpWin", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            // 先把这个连接对应的 frpc 停掉，否则它的配置文件会被删、进程还在跑
            FrpRunner runner;
            if (_clientRunners.TryGetValue(c.Id, out runner))
            {
                try { runner.Stop(); runner.Dispose(); } catch { }
                _clientRunners.Remove(c.Id);
            }

            int idx = _settings.Client.Selected;
            _settings.Client.Connections.RemoveAt(idx);
            if (idx >= _settings.Client.Connections.Count) idx = _settings.Client.Connections.Count - 1;
            _settings.Client.Selected = idx;

            RefreshConnectionList();
            LoadCurrentConnectionIntoUi();
            AppendLog(_cLog, Loc.F("已删除服务端连接「{0}」。", c.Label));
            RefreshStatus();
        }

        private void RenameConnection()
        {
            ClientConnection c = CurrentConnection();
            using (var dlg = new TextInputDialog("重命名服务端连接", "名称", c.Label))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = dlg.Value;
                if (name.Length == 0) { Warn("名称不能为空。"); return; }
                if (!string.Equals(name, c.Label, StringComparison.OrdinalIgnoreCase) && LabelExists(name))
                {
                    Warn(Loc.F("已经有一个叫「{0}」的连接了，请换一个名字。", name));
                    return;
                }
                c.Label = name;
            }
            RefreshConnectionList();
            AppendLog(_cLog, Loc.F("连接已重命名为「{0}」。", c.Label));
        }

        private static void SelectLevel(ComboBox box, string level)
        {
            int i = Array.IndexOf(LogLevels, (level ?? "").Trim().ToLowerInvariant());
            box.SelectedIndex = i >= 0 ? i : 2;
        }

        private bool SaveServerSettings()
        {
            var s = _settings.Server;
            s.BindAddr = string.IsNullOrWhiteSpace(_sBindAddr.Text) ? "0.0.0.0" : _sBindAddr.Text.Trim();

            int v;
            if (!ReadPort(_sBindPort, "监听端口 bindPort", 1, 65535, out v)) return false;
            s.BindPort = v;
            if (!ReadPort(_sKcpPort, "KCP 端口（不需要就填 0）", 0, 65535, out v)) return false;
            s.KcpBindPort = v;
            if (!ReadPort(_sHttpPort, "HTTP 虚拟主机端口（不需要就填 0）", 0, 65535, out v)) return false;
            s.VhostHttpPort = v;
            if (!ReadPort(_sHttpsPort, "HTTPS 虚拟主机端口（不需要就填 0）", 0, 65535, out v)) return false;
            s.VhostHttpsPort = v;
            if (!ReadPort(_sDashPort, "Dashboard 端口（不需要就填 0）", 0, 65535, out v)) return false;
            s.DashboardPort = v;

            s.SubDomainHost = _sSubDomain.Text.Trim();
            s.Token = _sToken.Text.Trim();
            s.DashboardUser = _sDashUser.Text.Trim();
            s.DashboardPassword = _sDashPass.Text;
            s.LogLevel = Convert.ToString(_sLogLevel.SelectedItem);
            return true;
        }

        private bool SaveClientSettings()
        {
            // 先用输入框校验当前显示的连接，这样报错能直接指到是哪个框填错了
            int v;
            if (!ReadPort(_cPort, "服务器端口 serverPort", 1, 65535, out v)) return false;
            if (!ReadPort(_cAdminPort, "本地管理界面端口（不需要就填 0）", 0, 65535, out v)) return false;

            StoreCurrentConnectionFromUi();

            for (int i = 0; i < _settings.Client.Connections.Count; i++)
            {
                ClientConnection c = _settings.Client.Connections[i];
                if (string.IsNullOrEmpty(c.Label)) c.Label = ConfigStore.DefaultLabel(i);
                if (!ValidateConnection(c, i)) return false;
            }
            return true;
        }

        /// <summary>校验一个服务端连接。index 用来判断它是不是界面上正在显示的那个。</summary>
        private bool ValidateConnection(ClientConnection c, int index)
        {
            bool isCurrent = index == _settings.Client.Selected;

            if (string.IsNullOrWhiteSpace(c.ServerAddr))
            {
                Warn(Loc.F("服务端连接「{0}」还没有填服务器地址（服务端的公网 IP 或域名）。", c.Label));
                if (isCurrent) { _cAddr.Focus(); _cAddr.SelectAll(); }
                return false;
            }
            if (c.ServerPort < 1 || c.ServerPort > 65535)
            {
                Warn(Loc.F("服务端连接「{0}」的服务器端口不合法，应该是 1~65535 的数字。", c.Label));
                if (isCurrent) { _cPort.Focus(); _cPort.SelectAll(); }
                return false;
            }
            if (c.AdminPort < 0 || c.AdminPort > 65535)
            {
                Warn(Loc.F("服务端连接「{0}」的本地管理界面端口不合法，应该是 0~65535 的数字。", c.Label));
                if (isCurrent) { _cAdminPort.Focus(); _cAdminPort.SelectAll(); }
                return false;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in c.Proxies)
            {
                if (p == null) continue;
                p.Name = (p.Name ?? "").Trim();
                if (p.Name.Length == 0)
                {
                    Warn(Loc.F("连接「{0}」的代理规则里有名称为空的行，请填写名称。", c.Label));
                    return false;
                }
                if (!names.Add(p.Name))
                {
                    Warn(Loc.F("连接「{0}」里有重复的代理名称：「{1}」，名称必须唯一。", c.Label, p.Name));
                    return false;
                }
                if (p.LocalPort <= 0 || p.LocalPort > 65535)
                {
                    Warn(Loc.F("连接「{0}」的代理「{1}」本地端口不合法（{2}）。", c.Label, p.Name, p.LocalPort));
                    return false;
                }
                string t = (p.Type ?? "tcp").ToLowerInvariant();
                if ((t == "tcp" || t == "udp") && (p.RemotePort <= 0 || p.RemotePort > 65535))
                {
                    Warn(Loc.F("连接「{0}」的代理「{1}」是 {2} 类型，必须填写 1~65535 的远程端口。", c.Label, p.Name, t));
                    return false;
                }
                if ((t == "http" || t == "https") &&
                    string.IsNullOrWhiteSpace(p.CustomDomains) && string.IsNullOrWhiteSpace(p.Subdomain))
                {
                    Warn(Loc.F("连接「{0}」的代理「{1}」是 {2} 类型，必须填写自定义域名或子域名。", c.Label, p.Name, t));
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(p.LocalIP)) p.LocalIP = p.LocalIP.Trim();
                if (!string.IsNullOrWhiteSpace(p.CustomDomains)) p.CustomDomains = p.CustomDomains.Trim();
                if (!string.IsNullOrWhiteSpace(p.Subdomain)) p.Subdomain = p.Subdomain.Trim();
                if (!string.IsNullOrWhiteSpace(p.SecretKey)) p.SecretKey = p.SecretKey.Trim();
            }

            foreach (var vis in c.Visitors)
            {
                if (vis == null) continue;
                vis.Name = (vis.Name ?? "").Trim();
                vis.ServerName = (vis.ServerName ?? "").Trim();
                if (vis.Name.Length == 0)
                {
                    Warn(Loc.F("连接「{0}」的访问者表格里有名称为空的行，请填写名称或删除该行。", c.Label));
                    return false;
                }
                if (vis.ServerName.Length == 0)
                {
                    Warn(Loc.F("连接「{0}」的访问者「{1}」必须填写对应的代理名 serverName。", c.Label, vis.Name));
                    return false;
                }
                if (vis.BindPort <= 0 || vis.BindPort > 65535)
                {
                    Warn(Loc.F("连接「{0}」的访问者「{1}」本地绑定端口不合法（{2}）。", c.Label, vis.Name, vis.BindPort));
                    return false;
                }
            }

            return true;
        }

        /// <summary>保存设置并重新生成 frps.toml 和每个连接的 frpc-*.toml。</summary>
        private bool SaveAll(string roleHint)
        {
            if (!SaveServerSettings()) return false;
            if (!SaveClientSettings()) return false;

            try
            {
                ConfigStore.Save(_settings);
                string f1;
                List<string> f2;
                ConfigStore.WriteToml(_settings, out f1, out f2);
                AppendLog(_sLog, "配置已保存：" + f1);
                for (int i = 0; i < f2.Count; i++)
                {
                    string label = i < _settings.Client.Connections.Count ? _settings.Client.Connections[i].Label : "";
                    AppendLog(_cLog, Loc.F("配置已保存：{0}（{1}）", f2[i], label));
                }
                return true;
            }
            catch (Exception ex)
            {
                Warn(Loc.F("保存配置失败：\r\n{0}", ex.Message));
                return false;
            }
        }

        // =====================================================================
        //  启动 / 停止
        // =====================================================================
        private void StartServer()
        {
            if (!SaveAll("frps")) return;

            if (!File.Exists(AppPaths.FrpsExe))
            {
                Warn(Loc.F("找不到 frps.exe：\r\n{0}\r\n\r\n请重新运行安装程序。", AppPaths.FrpsExe));
                return;
            }

            if (_serverRunner.IsRunning)
            {
                Info("服务端已经在运行中。");
                return;
            }

            try
            {
                _serverRunner.Start("frps", AppPaths.FrpsExe, AppPaths.FrpsConfig, AppPaths.InstallDir, null);
                AppendLog(_sLog, Loc.F("已启动 frps（PID {0}）。", _serverRunner.ProcessId));
                AppendLog(_sLog, Loc.F("若这是一台有公网 IP 的服务器，请记得用“放行防火墙端口”开放 {0} 端口。", _sBindPort.Text));
            }
            catch (Exception ex)
            {
                Warn(Loc.F("启动 frps 失败：\r\n{0}", ex.Message));
            }
            RefreshStatus();
        }

        private void StopServer()
        {
            if (!_serverRunner.IsRunning) { Info("服务端当前没有在运行。"); return; }
            _serverRunner.Stop();
            AppendLog(_sLog, "已停止 frps。");
            RefreshStatus();
        }

        /// <summary>给每个「已启用」的服务端连接各起一个 frpc 进程。</summary>
        private void StartClient()
        {
            if (!SaveAll("frpc")) return;

            if (!File.Exists(AppPaths.FrpcExe))
            {
                Warn(Loc.F("找不到 frpc.exe：\r\n{0}\r\n\r\n请重新运行安装程序。", AppPaths.FrpcExe));
                return;
            }

            string frpsPath;
            List<string> cfgPaths;
            ConfigStore.WriteToml(_settings, out frpsPath, out cfgPaths);

            int enabled = 0, started = 0, already = 0, failed = 0;

            for (int i = 0; i < _settings.Client.Connections.Count; i++)
            {
                ClientConnection c = _settings.Client.Connections[i];
                if (!c.Enabled) continue;
                enabled++;

                if (i >= cfgPaths.Count) continue;

                FrpRunner runner = RunnerFor(c);
                if (runner.IsRunning) { already++; continue; }

                try
                {
                    runner.Start("frpc", AppPaths.FrpcExe, cfgPaths[i], AppPaths.InstallDir, null, c.Label);
                    AppendLog(_cLog, Loc.F("[{0}] 已启动 frpc → {1}:{2}（PID {3}）。",
                        c.Label, c.ServerAddr, c.ServerPort, runner.ProcessId));
                    started++;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppendLog(_cLog, Loc.F("[{0}] 启动失败：{1}", c.Label, ex.Message));
                }
            }

            if (enabled == 0)
            {
                Warn(Loc.T("所有服务端连接都是「未启用」状态，没有可以启动的。\r\n\r\n") +
                     Loc.T("请在「连接设置」里选中一个连接并勾上「启用这个连接」。"));
            }
            else if (failed > 0)
            {
                Warn(Loc.F("启动了 {0} 个连接，{1} 个已经在跑，{2} 个启动失败。\r\n\r\n失败原因见下面的运行日志。",
                    started, already, failed));
            }
            else if (started > 0)
            {
                Info(Loc.F("已启动 {0} 个服务端连接（共 {1} 个已启用）。", started, enabled));
            }
            else
            {
                Info(Loc.F("{0} 个服务端连接都已经在运行中了。", already));
            }

            RefreshStatus();
        }

        private void StopClient()
        {
            if (RunningClientCount() == 0) { Info("客户端当前没有在运行。"); return; }

            StopAllClientRunners();
            AppendLog(_cLog, "已停止所有 frpc 进程。");
            RefreshStatus();
        }
    }

    /// <summary>一个简单的单行文本输入对话框（重命名连接用）。</summary>
    internal sealed class TextInputDialog : Form
    {
        private readonly TextBox _box = new TextBox { Width = 300 };

        public string Value { get { return (_box.Text ?? "").Trim(); } }

        public TextInputDialog(string title, string label, string value)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(380, 110);
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }

            _box.Text = value ?? "";

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) }, 0, 0);

            _box.Dock = DockStyle.Fill;
            _box.Margin = new Padding(3, 6, 3, 3);
            t.Controls.Add(_box, 1, 0);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 80, Margin = new Padding(3) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80, Margin = new Padding(3) };
            var flow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
            flow.Controls.Add(ok);
            flow.Controls.Add(cancel);
            t.Controls.Add(flow, 1, 1);

            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;

            Loc.Apply(this);
        }
    }

    /// <summary>修改 frpc 本地管理界面的账号密码。</summary>
    internal sealed class AdminAccountDialog : Form
    {
        private readonly TextBox _user = new TextBox { Width = 200 };
        private readonly TextBox _pass = new TextBox { Width = 200, UseSystemPasswordChar = true };

        public string UserName { get { return _user.Text; } }
        public string Password { get { return _pass.Text; } }

        public AdminAccountDialog(string user, string pass)
        {
            Text = "本地管理界面账号";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(340, 130);
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }

            _user.Text = user;
            _pass.Text = pass;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = "用户名", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            t.Controls.Add(_user, 1, 0);
            t.Controls.Add(new Label { Text = "密码", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            t.Controls.Add(_pass, 1, 1);

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 80 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
            var flow = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right };
            flow.Controls.Add(ok);
            flow.Controls.Add(cancel);
            t.Controls.Add(flow, 1, 2);

            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;

            Loc.Apply(this);
        }
    }
}
