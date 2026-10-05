using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FrpWin
{
    /// <summary>
    /// 「导入 frpc 配置」对话框。
    ///
    /// 用法：把别的机器上的 frpc.toml（或 frpc.ini / 一段配置文本）粘进来，
    /// 界面会当场解析出「服务端地址 / token / 代理规则 / 访问者」并显示数量，
    /// 然后由用户选择「新添一个服务端连接」还是「并进当前连接」。
    /// </summary>
    internal sealed class FrpcImportForm : Form
    {
        private readonly TextBox _text = new TextBox();
        private readonly RadioButton _rbNew = new RadioButton();
        private readonly RadioButton _rbMerge = new RadioButton();
        private readonly Label _preview = new Label();
        private readonly Button _ok = new Button();

        private FrpcConfigFile.ImportResult _result = new FrpcConfigFile.ImportResult();

        public FrpcConfigFile.ImportResult Result { get { return _result; } }

        /// <summary>true = 新添一个服务端连接，false = 并进当前连接。</summary>
        public bool ImportAsNewConnection { get { return _rbNew.Checked; } }

        public FrpcImportForm(string currentLabel)
        {
            Text = "导入 frpc 配置";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(780, 620);
            MinimumSize = new Size(620, 480);
            try { Font = new Font("Microsoft YaHei UI", 9F); } catch { }

            var root = new TableLayoutPanel
            {
                Name = "impRoot",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            // 单列也必须给 ColumnStyle，否则某一行的宽控件会把整列撑出窗体
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 说明
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 工具条
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // 文本框
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 预览 + 目标
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 按钮

            var tip = new Label
            {
                Name = "impTip",
                AutoSize = true,
                MaximumSize = new Size(740, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 0, 3, 6),
                Text = "把别的机器上的 frpc.toml 内容粘进下面的框，或者点「选择配置文件」直接挑文件；" +
                       "程序会自动认出里面的服务器地址、token、代理规则和访问者，不用一条条重新填。"
            };
            root.Controls.Add(tip, 0, 0);

            var bar = new FlowLayoutPanel
            {
                Name = "impBar",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0)
            };
            var pick = new Button { Name = "impPick", Text = "选择配置文件…", AutoSize = true, MinimumSize = new Size(130, 32), Margin = new Padding(3, 3, 6, 3) };
            var fromData = new Button { Name = "impFromData", Text = "读取本机已保存的配置", AutoSize = true, MinimumSize = new Size(160, 32), Margin = new Padding(3, 3, 6, 3) };
            var clear = new Button { Name = "impClear", Text = "清空", AutoSize = true, MinimumSize = new Size(70, 32), Margin = new Padding(3, 3, 6, 3) };
            pick.Click += (s, e) => PickFile();
            fromData.Click += (s, e) => PickFromDataDir();
            clear.Click += (s, e) => { _text.Text = ""; UpdatePreview(); };
            bar.Controls.Add(pick);
            bar.Controls.Add(fromData);
            bar.Controls.Add(clear);
            root.Controls.Add(bar, 0, 1);

            _text.Name = "impText";
            _text.Multiline = true;
            _text.ScrollBars = ScrollBars.Both;
            _text.WordWrap = false;
            _text.AcceptsReturn = true;
            _text.AcceptsTab = true;
            _text.Dock = DockStyle.Fill;
            _text.Margin = new Padding(3, 3, 3, 8);
            try { _text.Font = new Font("Consolas", 9.5F); } catch { }
            _text.TextChanged += (s, e) => UpdatePreview();
            root.Controls.Add(_text, 0, 2);

            var bottom = new TableLayoutPanel
            {
                Name = "impBottom",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowCount = 2;

            _preview.Name = "impPreview";
            _preview.AutoSize = true;
            _preview.MaximumSize = new Size(740, 0);
            _preview.Margin = new Padding(3, 3, 3, 6);
            _preview.Text = "还没有内容。";
            bottom.Controls.Add(_preview, 0, 0);

            var target = new FlowLayoutPanel
            {
                Name = "impTarget",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0)
            };
            _rbNew.Name = "impRbNew";
            _rbNew.Text = "新添一个服务端连接";
            _rbNew.AutoSize = true;
            _rbNew.Checked = true;
            _rbNew.Margin = new Padding(3, 3, 20, 3);
            _rbMerge.Name = "impRbMerge";
            _rbMerge.Text = "并进当前连接「" + (currentLabel ?? "") + "」";
            _rbMerge.AutoSize = true;
            _rbMerge.Margin = new Padding(3, 3, 3, 3);
            target.Controls.Add(_rbNew);
            target.Controls.Add(_rbMerge);
            bottom.Controls.Add(target, 0, 1);
            root.Controls.Add(bottom, 0, 3);

            var buttons = new FlowLayoutPanel
            {
                Name = "impButtons",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 6, 0, 0)
            };
            _ok.Name = "impOk";
            _ok.Text = "导入";
            _ok.AutoSize = true;
            _ok.MinimumSize = new Size(90, 32);
            _ok.Margin = new Padding(3);
            _ok.Click += (s, e) => TryAccept();
            var cancel = new Button { Name = "impCancel", Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 32), Margin = new Padding(3) };
            buttons.Controls.Add(_ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 4);

            Controls.Add(root);
            CancelButton = cancel;

            Loc.Apply(this);
            UpdatePreview();
        }

        /// <summary>给布局自检 / 截图用：直接填一段示例配置。</summary>
        internal void LoadSampleForTest(string content)
        {
            _text.Text = content ?? "";
            UpdatePreview();
        }

        // ---------------------------------------------------------------------
        private void PickFile()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = Loc.T("选择 frpc 配置文件");
                dlg.Filter = Loc.T("frp 配置文件 (*.toml;*.ini;*.conf)|*.toml;*.ini;*.conf|所有文件 (*.*)|*.*");
                try { dlg.InitialDirectory = AppPaths.DataDir; } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var bytes = File.ReadAllBytes(dlg.FileName);
                    string content;
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        content = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
                    else
                        content = new UTF8Encoding(false, false).GetString(bytes);
                    _text.Text = content;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.F("读取文件失败：{0}", ex.Message), "FrpWin",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void PickFromDataDir()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = Loc.T("读取本机已保存的 frpc 配置");
                dlg.Filter = Loc.T("frp 配置文件 (*.toml)|*.toml|所有文件 (*.*)|*.*");
                try { dlg.InitialDirectory = AppPaths.DataDir; } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { _text.Text = File.ReadAllText(dlg.FileName, Encoding.UTF8); }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.F("读取文件失败：{0}", ex.Message), "FrpWin",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// 边粘贴边识别：每次文本变化都重新解析一遍（配置文件一般只有几 KB）。
        /// 识别结果直接显示在下面，用户不用点按钮就能看到认出了几条规则。
        /// </summary>
        private void UpdatePreview()
        {
            string content = _text.Text ?? "";
            if (content.Trim().Length == 0)
            {
                _result = new FrpcConfigFile.ImportResult();
                _preview.Text = Loc.T("还没有内容。");
                _ok.Enabled = false;
                return;
            }

            FrpcConfigFile.ImportResult r;
            try { r = FrpcConfigFile.ParseText(content, AppPaths.DataDir); }
            catch (Exception ex)
            {
                _result = new FrpcConfigFile.ImportResult();
                _preview.Text = Loc.F("解析失败：{0}", ex.Message);
                _ok.Enabled = false;
                return;
            }

            _result = r;
            _ok.Enabled = r.HasAnything;

            var sb = new StringBuilder();
            if (r.HasAnything)
            {
                if (r.HasServer && !string.IsNullOrWhiteSpace(r.ServerAddr))
                    sb.AppendLine(Loc.F("识别到服务器：{0}:{1}", r.ServerAddr, r.ServerPort > 0 ? r.ServerPort : 7000));
                else
                    sb.AppendLine(Loc.T("这份配置里没有服务器地址（服务端信息可能写在别的文件里），导入后请自己填。"));

                if (r.HasToken) sb.AppendLine(Loc.T("识别到认证令牌（token），已一并导入。"));
                if (r.HasTls) sb.AppendLine(Loc.F("TLS 加密：{0}", Loc.T(r.TlsEnable ? "开" : "关")));
                if (r.AdminPort > 0) sb.AppendLine(Loc.F("本地管理界面端口：{0}", r.AdminPort));

                sb.AppendLine(Loc.F("识别到 {0} 条代理规则、{1} 条访问者。", r.Proxies.Count, r.Visitors.Count));
                if (r.ExtraKeyCount > 0)
                    sb.AppendLine(Loc.F("有 {0} 个界面外的参数（healthCheck、metadatas 之类）会原样保留。", r.ExtraKeyCount));
                if (r.Files.Count > 1)
                    sb.AppendLine(Loc.F("（includes 展开了 {0} 个文件）", r.Files.Count));
            }
            else
            {
                sb.AppendLine(Loc.T("没有识别到任何代理规则或服务器信息，请确认粘进来的是一份 frpc 配置。"));
            }
            // 注意用 Format 而不是直接赋 StringBuilder.ToString()：
            // AutoSize 标签量多行文本时只按最后一行算宽，换行会被吃掉。
            _preview.Text = string.Format("{0}", sb).TrimEnd();
        }

        private void TryAccept()
        {
            if (!_result.HasAnything)
            {
                MessageBox.Show(Loc.T("没有识别到可导入的内容。"), "FrpWin",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
