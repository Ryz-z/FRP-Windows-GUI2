using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace FrpWin
{
    /// <summary>两个对话框共用的小工具。</summary>
    internal static class FwUi
    {
        /// <summary>
        /// 表格的表头高度、行高都必须按“当前字体”算出来，否则继承 9pt 微软雅黑之后
        /// 表头文字会被纵向截掉一半（第二版修过一次的坑，这里保持一致）。
        /// </summary>
        public static DataGridView MakeGrid(Font font)
        {
            int line = MainForm.LineHeight(font);

            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = font,
                RowTemplate = { Height = line + 9 }
            };
            g.DataError += (s, e) => { e.ThrowException = false; };
            g.FontChanged += (s, e) => { g.RowTemplate.Height = MainForm.LineHeight(g.Font) + 9; };
            return g;
        }

        public static DataGridViewTextBoxColumn Text(string header, string prop, int weight)
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

        public static DataGridViewCheckBoxColumn Check(string header, int weight)
        {
            return new DataGridViewCheckBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FalseValue = false,
                TrueValue = true
            };
        }

        public static DataGridViewComboBoxColumn Combo(string header, int weight, string[] items)
        {
            var col = new DataGridViewComboBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                MinimumWidth = weight,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ValueType = typeof(string)
            };
            col.Items.AddRange(items);
            return col;
        }

        /// <summary>
        /// 可换行的说明标签。用 MaximumSize 控制折行宽度（和主窗口里的提示标签同一套做法），
        /// 并跟着对话框宽度实时调整，窗口拉窄了也不会把文字裁掉。
        /// </summary>
        public static Label WrapLabel(Control owner, int reservedWidth)
        {
            var label = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(4, 4, 4, 4),
                MaximumSize = new Size(Math.Max(240, owner.ClientSize.Width - reservedWidth), 0)
            };
            owner.SizeChanged += (s, e) =>
            {
                label.MaximumSize = new Size(Math.Max(240, owner.ClientSize.Width - reservedWidth), 0);
            };
            return label;
        }

        /// <summary>给 TableLayoutPanel 补 RowStyles 的小助手（直接改 RowCount 不会生成样式）。</summary>
        public static TableLayoutPanel Rows(TableLayoutPanel t, params SizeType[] kinds)
        {
            t.RowStyles.Clear();
            foreach (SizeType k in kinds)
                t.RowStyles.Add(k == SizeType.Percent ? new RowStyle(SizeType.Percent, 100F) : new RowStyle(SizeType.AutoSize));
            t.RowCount = t.RowStyles.Count;
            return t;
        }

        /// <summary>
        /// 对话框的根容器：单列、占满、行样式显式声明。
        /// 必须显式写 ColumnStyles，否则 TableLayoutPanel 的单列会退化成 AutoSize：
        /// 只要有一个子控件的“理想宽度”比窗口还宽（例如一行放不下的单选按钮组），
        /// 整列就会被撑开，所有子控件跟着溢出到窗口外面去。
        /// </summary>
        public static TableLayoutPanel RootForm(params SizeType[] rowKinds)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(10)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return Rows(t, rowKinds);
        }
    }

    // ======================================================================
    //  ① 选择客户端
    // ======================================================================
    internal sealed class ClientPickForm : Form
    {
        /// <summary>绑定到客户端表格的一行。</summary>
        internal sealed class Row
        {
            public string Name { get; set; }
            public string HostName { get; set; }
            public string ClientIP { get; set; }
            public string Version { get; set; }
            public string Online { get; set; }
            public string ProxyCount { get; set; }
            public string Ports { get; set; }

            [Browsable(false)]
            public FrpsClientInfo Client { get; set; }

            public Row()
            {
                Name = ""; HostName = ""; ClientIP = ""; Version = "";
                Online = ""; ProxyCount = ""; Ports = "";
            }
        }

        private readonly Action<string> _log;
        private readonly Func<bool> _ensureAdmin;

        private TextBox _txtHost, _txtPort, _txtUser, _txtPass;
        private DataGridView _grid;
        private BindingList<Row> _rows = new BindingList<Row>();
        private Label _status;
        private Button _btnNext;

        private List<FrpsClientInfo> _clients = new List<FrpsClientInfo>();
        private List<FrpsProxyInfo> _proxies = new List<FrpsProxyInfo>();

        public ClientPickForm(string host, int port, string user, string password, Action<string> log, Func<bool> ensureAdmin)
        {
            _log = log;
            _ensureAdmin = ensureAdmin;

            Text = "一键放行客户端端口 —— ① 选择客户端";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 580);
            MinimumSize = new Size(920, 440);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }

            // ---------------- Dashboard 连接区 ----------------
            _txtHost = new TextBox { Text = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(), Name = "cliHost", Dock = DockStyle.Fill };
            _txtPort = new TextBox { Text = port > 0 ? port.ToString() : "7500", Name = "cliPort", Dock = DockStyle.Fill };
            _txtUser = new TextBox { Text = user ?? "", Name = "cliUser", Dock = DockStyle.Fill };
            _txtPass = new TextBox { Text = password ?? "", Name = "cliPass", Dock = DockStyle.Fill, UseSystemPasswordChar = true };

            var connTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 9,
                Margin = new Padding(0)
            };
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            // 最后一列留空吃掉多余宽度，免得被拉伸的是“密码”输入框
            connTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            connTable.Controls.Add(FieldLabel("Dashboard 地址"), 0, 0);
            connTable.Controls.Add(_txtHost, 1, 0);
            connTable.Controls.Add(FieldLabel("端口"), 2, 0);
            connTable.Controls.Add(_txtPort, 3, 0);
            connTable.Controls.Add(FieldLabel("用户名"), 4, 0);
            connTable.Controls.Add(_txtUser, 5, 0);
            connTable.Controls.Add(FieldLabel("密码"), 6, 0);
            connTable.Controls.Add(_txtPass, 7, 0);

            var connBox = new GroupBox
            {
                Name = "cliConn",
                Text = "服务端 Dashboard（frps 管理接口，图形界面默认连本机）",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };
            connBox.Controls.Add(connTable);

            var reloadBtn = MainForm.MakeButton("重新读取", 100, false);
            reloadBtn.Name = "cliReload";
            reloadBtn.Click += (s, e) => Reload();

            var toolbar = new FlowLayoutPanel { Name = "cliToolbar", Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            toolbar.Controls.Add(reloadBtn);

            var hint = FwUi.WrapLabel(this, 60);
            hint.Name = "cliHint";
            hint.Text = Loc.T("这里的地址/账号对应“服务端”标签页里的 Dashboard 端口、用户名、密码；") +
                        Loc.T("先保存并启动服务端，再点“重新读取”，下面就会列出所有连上来的客户端。");

            // ---------------- 客户端表格 ----------------
            _grid = FwUi.MakeGrid(Font);
            _grid.Name = "cliGrid";
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.ReadOnly = true;
            _grid.Columns.Add(FwUi.Text("客户端", "Name", 130));
            _grid.Columns.Add(FwUi.Text("主机名", "HostName", 165));
            _grid.Columns.Add(FwUi.Text("客户端 IP", "ClientIP", 145));
            _grid.Columns.Add(FwUi.Text("版本", "Version", 65));
            _grid.Columns.Add(FwUi.Text("状态", "Online", 55));
            _grid.Columns.Add(FwUi.Text("代理", "ProxyCount", 55));
            _grid.Columns.Add(FwUi.Text("需要放行的端口", "Ports", 420));
            _grid.DataSource = _rows;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) NextStep(); };

            _status = FwUi.WrapLabel(this, 60);
            _status.Name = "cliStatus";
            _status.ForeColor = Color.FromArgb(0, 90, 160);
            _status.Text = "";

            _btnNext = MainForm.MakeButton("下一步：选择端口 ▶", 170, true);
            _btnNext.Name = "cliNext";
            _btnNext.Click += (s, e) => NextStep();

            var manual = MainForm.MakeButton("手工输入端口…", 140, false);
            manual.Name = "cliManual";
            manual.Click += (s, e) => ManualStep();

            var close = MainForm.MakeButton("关闭", 90, false);
            close.Name = "cliClose";
            close.DialogResult = DialogResult.Cancel;

            var buttons = new FlowLayoutPanel { Name = "cliButtons", Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            buttons.Controls.Add(_btnNext);
            buttons.Controls.Add(manual);
            buttons.Controls.Add(close);

            var root = FwUi.RootForm(SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            root.Controls.Add(connBox, 0, 0);
            root.Controls.Add(toolbar, 0, 1);
            root.Controls.Add(_grid, 0, 2);
            root.Controls.Add(hint, 0, 3);
            root.Controls.Add(_status, 0, 4);
            root.Controls.Add(buttons, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            AcceptButton = _btnNext;

            Loc.Apply(this);
            Shown += (s, e) => { if (_autoReload) Reload(); };
        }

        private bool _autoReload = true;

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 6, 3) };
        }

        /// <summary>测试用：直接注入数据，不访问网络。</summary>
        internal void LoadForTest(List<FrpsClientInfo> clients, List<FrpsProxyInfo> proxies)
        {
            _autoReload = false;
            _clients = clients ?? new List<FrpsClientInfo>();
            _proxies = proxies ?? new List<FrpsProxyInfo>();
            BuildRows();
            _status.Text = Loc.F("（测试数据）共 {0} 个客户端。", _rows.Count);
        }

        internal void SelectFirstForTest()
        {
            if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }

        // ------------------------------------------------------------------

        private void Reload()
        {
            int port;
            if (!int.TryParse((_txtPort.Text ?? "").Trim(), out port) || port < 1 || port > 65535)
            {
                _status.ForeColor = Color.Firebrick;
                _status.Text = Loc.T("Dashboard 端口必须是 1~65535 的数字。");
                _txtPort.Focus();
                _txtPort.SelectAll();
                return;
            }

            _status.ForeColor = Color.FromArgb(0, 90, 160);
            _status.Text = Loc.F("正在读取 {0}:{1} …", _txtHost.Text.Trim(), port);
            _status.Update();
            Cursor = Cursors.WaitCursor;

            try
            {
                var api = new FrpsApi(_txtHost.Text, port, _txtUser.Text, _txtPass.Text);
                string version = api.ProbeServerInfo();
                _clients = api.GetClients();
                _proxies = api.GetProxies();
                BuildRows();

                _status.ForeColor = Color.FromArgb(0, 120, 0);
                _status.Text = Loc.F("连接成功：frps {0}，读到 {1} 个客户端、{2} 条代理规则。",
                                     string.IsNullOrEmpty(version) ? Loc.T("(未知版本)") : version,
                                     _rows.Count, _proxies.Count) +
                               (ServiceManager.IsAdministrator
                                    ? ""
                                    : Loc.T("  当前不是管理员，最后一步“一键开启”会提示以管理员身份重启。"));

                if (_log != null)
                    _log(Loc.F("已从 Dashboard 读取到 {0} 个客户端、{1} 条代理规则。", _rows.Count, _proxies.Count));
            }
            catch (Exception ex)
            {
                _clients = new List<FrpsClientInfo>();
                _proxies = new List<FrpsProxyInfo>();
                BuildRows();

                _status.ForeColor = Color.Firebrick;
                _status.Text = Loc.F("读取失败：{0}", FrpsApi.Describe(ex));

                if (_log != null) _log(Loc.F("读取 frps Dashboard 失败：{0}", FrpsApi.Describe(ex).Replace("\r\n", " ")));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void BuildRows()
        {
            var rows = new List<Row>();
            foreach (FrpsClientInfo c in _clients)
            {
                List<FrpsProxyInfo> mine = ClientPortPlan.ProxiesOf(c, _proxies);
                var withPort = new List<FrpsProxyInfo>();

                foreach (FrpsProxyInfo p in mine)
                {
                    if (p.HasServerPort) withPort.Add(p);
                }

                // 按端口号排序，读起来才顺
                withPort.Sort((a, b) =>
                {
                    int n = a.RemotePort.CompareTo(b.RemotePort);
                    return n != 0 ? n : string.CompareOrdinal(a.Protocol, b.Protocol);
                });

                var ports = new List<string>();
                foreach (FrpsProxyInfo p in withPort) ports.Add(p.RemotePort + "/" + p.Protocol);

                rows.Add(new Row
                {
                    Name = c.Label,
                    HostName = c.Hostname,
                    ClientIP = c.ClientIP,
                    Version = c.Version,
                    Online = c.Online ? "在线" : "离线",
                    ProxyCount = mine.Count.ToString(),
                    Ports = ports.Count > 0 ? string.Join("、", ports.ToArray()) : "无",
                    Client = c
                });
            }

            _rows = new BindingList<Row>(rows);
            _grid.DataSource = _rows;

            if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[0].Cells[0];
            _btnNext.Enabled = _rows.Count > 0;
        }

        private Row SelectedRow()
        {
            if (_grid.CurrentRow == null) return null;
            return _grid.CurrentRow.DataBoundItem as Row;
        }

        private void NextStep()
        {
            Row row = SelectedRow();
            if (row == null || row.Client == null)
            {
                MainForm.Warn("请先在列表里选中一个客户端。");
                return;
            }

            string note;
            List<PortEntry> entries = ClientPortPlan.Build(row.Client, _proxies, out note);

            string title = row.Client.Label;
            if (!string.IsNullOrEmpty(row.Client.Hostname) && !string.Equals(row.Client.Hostname, title, StringComparison.Ordinal))
                title = Loc.F("{0}（主机名 {1}）", title, row.Client.Hostname);
            if (!string.IsNullOrEmpty(row.Client.ClientIP))
                title = Loc.F("{0}（IP {1}）", title, row.Client.ClientIP);

            using (var dlg = new ClientPortFirewallForm(title, entries, note, _log, _ensureAdmin))
            {
                dlg.ShowDialog(this);
            }
        }

        private void ManualStep()
        {
            using (var dlg = new ClientPortFirewallForm("手工指定的端口", new List<PortEntry>(), "", _log, _ensureAdmin))
            {
                dlg.ShowDialog(this);
            }
        }
    }

    // ======================================================================
    //  ② 选择端口 + 一键开启
    // ======================================================================
    internal sealed class ClientPortFirewallForm : Form
    {
        private readonly Action<string> _log;
        private readonly Func<bool> _ensureAdmin;
        private readonly List<PortEntry> _entries;

        private DataGridView _grid;
        private CheckBox _chkAll;
        private Label _count;
        private RadioButton _rbIn, _rbOut, _rbBoth;
        private Label _status;
        private Button _btnApply;
        private bool _updating;

        public ClientPortFirewallForm(string clientTitle, List<PortEntry> entries, string note, Action<string> log, Func<bool> ensureAdmin)
        {
            _log = log;
            _ensureAdmin = ensureAdmin;
            _entries = entries ?? new List<PortEntry>();

            Text = "一键放行客户端端口 —— ② 选择端口";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(990, 660);
            MinimumSize = new Size(850, 540);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }

            // ---------------- 标题 / 说明 ----------------
            var info = FwUi.WrapLabel(this, 60);
            info.Name = "fwInfo";
            info.ForeColor = Color.Black;
            info.Text = Loc.F("客户端：{0}　—— 下面是这个客户端在服务端上占用的端口，勾选要放行的，再点最下面的按钮。",
                              clientTitle ?? "");
            if (!string.IsNullOrEmpty(note)) info.Text = info.Text + "\r\n" + note;

            // ---------------- 工具条 ----------------
            _chkAll = new CheckBox
            {
                Name = "fwAll",
                Text = "全选",
                AutoSize = true,
                ThreeState = true,
                AutoCheck = false,
                CheckState = CheckState.Checked,
                Margin = new Padding(3, 8, 14, 3)
            };
            _chkAll.Click += (s, e) => SetAll(_chkAll.CheckState != CheckState.Checked);

            var addBtn = MainForm.MakeButton("＋ 添加端口", 120, false);
            addBtn.Name = "fwAdd";
            addBtn.Click += (s, e) => AddRow();

            var delBtn = MainForm.MakeButton("－ 删除选中行", 130, false);
            delBtn.Name = "fwDel";
            delBtn.Click += (s, e) => DeleteSelected();

            _count = new Label { Name = "fwCount", AutoSize = true, Margin = new Padding(14, 9, 3, 3), ForeColor = Color.DimGray };

            var toolbar = new FlowLayoutPanel { Name = "fwToolbar", Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            toolbar.Controls.Add(_chkAll);
            toolbar.Controls.Add(addBtn);
            toolbar.Controls.Add(delBtn);
            toolbar.Controls.Add(_count);

            // ---------------- 端口表格 ----------------
            _grid = FwUi.MakeGrid(Font);
            _grid.Name = "fwGrid";
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.AllowUserToAddRows = false;
            _grid.Columns.Add(FwUi.Check("放行", 50));
            _grid.Columns.Add(FwUi.Text("端口", "", 70));
            _grid.Columns.Add(FwUi.Combo("协议", 70, new[] { "TCP", "UDP" }));
            _grid.Columns.Add(FwUi.Text("代理名", "", 150));
            _grid.Columns.Add(FwUi.Text("说明", "", 330));
            _grid.Columns.Add(FwUi.Text("结果", "", 90));
            _grid.Columns[3].ReadOnly = true;
            _grid.Columns[4].ReadOnly = true;
            _grid.Columns[5].ReadOnly = true;

            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0) RefreshAllState();
            };
            _grid.RowsAdded += (s, e) => RefreshAllState();
            _grid.RowsRemoved += (s, e) => RefreshAllState();

            foreach (PortEntry entry in _entries) AddRow(entry);
            if (_grid.Rows.Count == 0) AddRow(null);            // 手工模式：给一个空行
            // 不要停在第一行的复选框上：当选中的单元格正好是复选框列时，
            // DataGridView 会把它画成“正在编辑”的样子，看起来像没勾上。
            _grid.CurrentCell = null;
            RefreshAllState();

            // ---------------- 方向 ----------------
            _rbIn = new RadioButton { Name = "fwRbIn", Text = "进站（允许外部访问本机的这些端口）", AutoSize = true, Checked = true, Margin = new Padding(3, 3, 20, 3) };
            _rbOut = new RadioButton { Name = "fwRbOut", Text = "出站（允许本机使用这些端口对外连接）", AutoSize = true, Margin = new Padding(3, 3, 20, 3) };
            _rbBoth = new RadioButton { Name = "fwRbBoth", Text = "进站 + 出站（两个方向都要）", AutoSize = true, Margin = new Padding(3, 3, 20, 3) };

            // 三个单选按钮用 TableLayoutPanel 竖着排。
            // 这里不能用 FlowLayoutPanel + AutoSize：它在“理想尺寸”阶段是按不折行算高度的，
            // 等真正布局时宽度不够、折成两行，高度却还是按一行给的，第二行就会压到下面的按钮上。
            var dirTable = new TableLayoutPanel
            {
                Name = "fwDirFlow",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0)
            };
            dirTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            FwUi.Rows(dirTable, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            dirTable.Controls.Add(_rbIn, 0, 0);
            dirTable.Controls.Add(_rbOut, 0, 1);
            dirTable.Controls.Add(_rbBoth, 0, 2);

            var dirBox = new GroupBox
            {
                Name = "fwDir",
                Text = "放行方向",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6)
            };
            dirBox.Controls.Add(dirTable);

            // ---------------- 按钮 ----------------
            _btnApply = MainForm.MakeButton("一键开启防火墙端口", 190, true);
            _btnApply.Name = "fwApply";
            _btnApply.Click += (s, e) => ApplyRules();

            var close = MainForm.MakeButton("关闭", 90, false);
            close.Name = "fwClose";
            close.DialogResult = DialogResult.Cancel;

            var buttons = new FlowLayoutPanel { Name = "fwButtons", Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            buttons.Controls.Add(_btnApply);
            buttons.Controls.Add(close);

            _status = FwUi.WrapLabel(this, 60);
            _status.Name = "fwStatus";
            _status.ForeColor = Color.FromArgb(0, 90, 160);
            _status.Text = Loc.T("规则名统一为 “FrpWin Client <端口> <TCP|UDP> <IN|OUT>”，可以在 “Windows 防火墙 → 高级安全” 里查看或删除。");

            var root = FwUi.RootForm(SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            root.Controls.Add(info, 0, 0);
            root.Controls.Add(toolbar, 0, 1);
            root.Controls.Add(_grid, 0, 2);
            root.Controls.Add(dirBox, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            root.Controls.Add(_status, 0, 5);

            Controls.Add(root);
            CancelButton = close;

            Loc.Apply(this);
        }

        // ------------------------------------------------------------------

        private void AddRow()
        {
            AddRow(null);
        }

        private void AddRow(PortEntry entry)
        {
            _grid.Rows.Add(
                entry == null ? true : entry.Allow,
                entry == null ? "" : entry.PortText,
                entry == null ? "TCP" : (entry.IsUdp ? "UDP" : "TCP"),
                entry == null ? "" : entry.ProxyName,
                entry == null ? Loc.T("手工添加：填端口号，选 TCP/UDP") : entry.Note,
                "");
        }

        private void DeleteSelected()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MainForm.Warn("请先在表格里选中要删除的行。");
                return;
            }

            var indexes = new List<int>();
            foreach (DataGridViewRow r in _grid.SelectedRows) if (!r.IsNewRow) indexes.Add(r.Index);
            indexes.Sort();
            for (int i = indexes.Count - 1; i >= 0; i--) _grid.Rows.RemoveAt(indexes[i]);

            if (_grid.Rows.Count == 0) AddRow(null);
            RefreshAllState();
        }

        private static bool RowChecked(DataGridViewRow row)
        {
            object v = row.Cells[0].Value;
            return v != null && v != DBNull.Value && Convert.ToBoolean(v);
        }

        private static string CellText(DataGridViewRow row, int index)
        {
            object v = row.Cells[index].Value;
            return v == null || v == DBNull.Value ? "" : Convert.ToString(v);
        }

        private void SetAll(bool value)
        {
            _updating = true;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    row.Cells[0].Value = value;
                }
            }
            finally { _updating = false; }
            RefreshAllState();
        }

        private void RefreshAllState()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                int total = 0, on = 0;
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    total++;
                    if (RowChecked(row)) on++;
                }

                if (total == 0 || on == 0) _chkAll.CheckState = CheckState.Unchecked;
                else if (on == total) _chkAll.CheckState = CheckState.Checked;
                else _chkAll.CheckState = CheckState.Indeterminate;

                if (_count != null) _count.Text = Loc.F("已勾选 {0} / {1} 个端口", on, total);
            }
            finally { _updating = false; }
        }

        private void ApplyRules()
        {
            _grid.EndEdit();

            var entries = new List<PortEntry>();
            var bad = new List<string>();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                var entry = new PortEntry
                {
                    Allow = RowChecked(row),
                    PortText = CellText(row, 1),
                    Protocol = CellText(row, 2),
                    ProxyName = CellText(row, 3)
                };

                if (entry.Allow && !entry.IsValidPort)
                    bad.Add(Loc.F("第 {0} 行“{1}”", row.Index + 1, entry.PortText));
                else
                    entries.Add(entry);
            }

            if (bad.Count > 0)
            {
                MainForm.Warn(Loc.F("下面这些行的端口不是 1~65535 的数字，请先改正（或取消勾选）：\r\n\r\n{0}",
                                    string.Join("\r\n", bad.ToArray())));
                return;
            }

            bool inbound = _rbIn.Checked || _rbBoth.Checked;
            bool outbound = _rbOut.Checked || _rbBoth.Checked;

            List<FirewallRule> rules = ClientPortPlan.ToRules(entries, inbound, outbound);
            if (rules.Count == 0)
            {
                MainForm.Warn("还没有勾选任何有效端口。请至少勾选一行，端口填 1~65535。");
                return;
            }

            if (_ensureAdmin != null && !_ensureAdmin()) return;

            List<FirewallApplyResult> results;
            Cursor = Cursors.WaitCursor;
            try { results = Firewall.Apply(rules, _log); }
            finally { Cursor = Cursors.Default; }

            // 把结果写回表格的“结果”列
            var okByKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            int ok = 0, fail = 0;
            foreach (FirewallApplyResult r in results)
            {
                if (r.Ok) ok++; else fail++;
                okByKey[r.Rule.Protocol + "/" + r.Rule.Port + "/" + (r.Rule.Inbound ? "in" : "out")] = r.Ok;
            }

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                if (!RowChecked(row)) { row.Cells[5].Value = ""; continue; }

                int port;
                if (!int.TryParse(CellText(row, 1).Trim(), out port)) { row.Cells[5].Value = ""; continue; }
                string proto = string.Equals(CellText(row, 2).Trim(), "UDP", StringComparison.OrdinalIgnoreCase) ? "UDP" : "TCP";

                bool allOk = true;
                bool any = false;
                if (inbound) { bool v; if (okByKey.TryGetValue(proto + "/" + port + "/in", out v)) { any = true; allOk &= v; } }
                if (outbound) { bool v; if (okByKey.TryGetValue(proto + "/" + port + "/out", out v)) { any = true; allOk &= v; } }

                row.Cells[5].Value = !any ? "" : (allOk ? "✔ 成功" : "✘ 失败");
            }

            _status.ForeColor = fail == 0 ? Color.FromArgb(0, 120, 0) : Color.Firebrick;
            _status.Text = Loc.F("处理完成：成功 {0} 条，失败 {1} 条（共 {2} 条规则）。", ok, fail, results.Count) +
                           (fail == 0
                                ? Loc.T("可以继续改勾选再点一次，规则不会重复堆积。")
                                : Loc.T("失败原因见“运行日志”。"));

            var msg = new System.Text.StringBuilder();
            msg.AppendLine(Loc.F("已在 Windows 防火墙中处理 {0} 条规则：成功 {1} 条，失败 {2} 条。",
                                 results.Count, ok, fail));
            msg.AppendLine();
            msg.AppendLine(Loc.F("方向：{0}", inbound && outbound
                ? Loc.T("进站 + 出站") : (inbound ? Loc.T("进站") : Loc.T("出站"))));
            msg.AppendLine(Loc.T("规则名：FrpWin Client <端口> <TCP|UDP> <IN|OUT>"));
            msg.AppendLine();
            msg.Append(Loc.T("可以在“Windows 防火墙 → 高级安全 → 入站规则 / 出站规则”里查看效果。"));

            if (fail == 0) MainForm.Info(msg.ToString());
            else MainForm.Warn(msg.ToString());
        }
    }

    // ======================================================================
    //  入口
    // ======================================================================
    internal static class ClientPortWizard
    {
        public static void Show(IWin32Window owner, string host, int port, string user, string password,
                                Action<string> log, Func<bool> ensureAdmin)
        {
            using (var pick = new ClientPickForm(host, port, user, password, log, ensureAdmin))
            {
                pick.ShowDialog(owner);
            }
        }
    }

    // ======================================================================
    //  自检数据：给 --dump-dialog / --screenshot-dialog 用，
    //  这样界面布局可以在没有真实 frps 的情况下被自动检查和截图。
    // ======================================================================
    internal static class ClientFirewallSample
    {
        public static void Build(out List<FrpsClientInfo> clients, out List<FrpsProxyInfo> proxies)
        {
            clients = new List<FrpsClientInfo>
            {
                new FrpsClientInfo { Key = "k1", User = "home-nas", Hostname = "NAS-01", ClientID = "2f8a1c4e-1111-2222",
                                     RunID = "r1", Version = "0.71.0", ClientIP = "192.168.1.10", Online = true },
                new FrpsClientInfo { Key = "k2", User = "office-pc", Hostname = "DESKTOP-8KQ2", ClientID = "77bd90aa-3333-4444",
                                     RunID = "r2", Version = "0.71.0", ClientIP = "10.0.0.5", Online = true },
                new FrpsClientInfo { Key = "k3", User = "", Hostname = "WIN-SERVER", ClientID = "9c02ff13-5555-6666",
                                     RunID = "r3", Version = "0.70.1", ClientIP = "172.16.0.9", Online = true }
            };

            // 示例数据和真实 frp 保持同形：tcp/udp 代理一般不带 localPort
            // （本地端口是客户端自己的事，frps 不记录），这里只留一个带 localPort 的样本。
            proxies = new List<FrpsProxyInfo>
            {
                P("tcp",  "nas-ssh",  "2f8a1c4e-1111-2222", "home-nas",  6000,  0),
                P("tcp",  "nas-web",  "2f8a1c4e-1111-2222", "home-nas",  8080,  80),
                P("udp",  "nas-game", "2f8a1c4e-1111-2222", "home-nas",  7100,  0),
                P("http", "nas-blog", "2f8a1c4e-1111-2222", "home-nas",  0,     80),
                P("stcp", "nas-p2p",  "2f8a1c4e-1111-2222", "home-nas",  0,     3306),
                P("tcp",  "pc-rdp",   "77bd90aa-3333-4444", "office-pc", 13389, 0),
                P("tcp",  "pc-files", "77bd90aa-3333-4444", "office-pc", 6045,  0),
                P("udp",  "pc-voice", "77bd90aa-3333-4444", "office-pc", 7200,  0),
                P("stcp", "srv-db",   "9c02ff13-5555-6666", "",          0,     5432)
            };

            FrpsProxyInfo legacy = P("tcp", "nas-legacy", "2f8a1c4e-1111-2222", "home-nas", 8090, 0);
            legacy.Status = "offline";
            proxies.Add(legacy);

            proxies[3].Domains.Add("blog.example.com");
        }

        private static FrpsProxyInfo P(string type, string name, string clientID, string user, int remote, int local)
        {
            return new FrpsProxyInfo
            {
                Type = type,
                Name = name,
                ClientID = clientID,
                User = user,
                Status = "online",
                RemotePort = remote,
                LocalPort = local,
                LocalIP = "127.0.0.1"
            };
        }
    }
}
