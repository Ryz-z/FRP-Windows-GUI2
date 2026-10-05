using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FrpWin
{
    /// <summary>
    /// 界面语言。
    ///
    /// 设计要点：**中文原文就是翻译表的主键**。
    /// 代码里照常写中文，`Apply()` 在建好窗口之后把可视树上的静态文字整棵换掉，
    /// 需要拼字符串（“共 3 个客户端”这类）的地方用 Loc.F 走同一张表。
    /// 这样中文原文始终留在代码里可读，所有译文集中在 LocTable.cs 一个文件，
    /// 也方便用自动化测试扫出“英文模式下还剩哪些中文没翻译”。
    ///
    /// 语言优先级：
    ///   1) ui-settings.xml 里的 Language（用户在程序里明确选过 zh / en）
    ///   2) 安装时选的语言（安装程序写到 ProgramData\FrpWin\language.txt）
    ///   3) 系统界面语言（中文系统 → 中文，其余 → 英文）
    /// </summary>
    internal static partial class Loc
    {
        public const string Auto = "auto";

        private static bool _en;
        private static string _lang = "zh";
        private static Dictionary<string, string> _map;

        public static bool IsEnglish { get { return _en; } }
        public static string Lang { get { return _lang; } }

        /// <summary>安装程序写下的语言选择文件。</summary>
        public static string InstallerLanguageFile
        {
            get { return Path.Combine(AppPaths.DataDir, "language.txt"); }
        }

        // ------------------------------------------------------------------
        //  初始化
        // ------------------------------------------------------------------

        /// <summary>lang 取值：auto / zh / en。</summary>
        public static void Init(string lang)
        {
            _lang = Resolve(lang);
            _en = _lang == "en";
        }

        /// <summary>把 auto 解析成 zh 或 en。</summary>
        public static string Resolve(string lang)
        {
            string l = (lang ?? "").Trim().ToLowerInvariant();
            if (l == "zh" || l == "zh-cn" || l == "zh-tw" || l == "chinese") return "zh";
            if (l == "en" || l == "en-us" || l == "english") return "en";

            string fromInstaller = ReadInstallerChoice();
            if (fromInstaller != null) return fromInstaller;

            try
            {
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
            }
            catch { }
            return "zh";
        }

        private static string ReadInstallerChoice()
        {
            try
            {
                string path = InstallerLanguageFile;
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path).Trim().ToLowerInvariant();
                if (text.StartsWith("en")) return "en";
                if (text.StartsWith("zh")) return "zh";
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------
        //  取词
        // ------------------------------------------------------------------

        /// <summary>按中文原文取译文；没有译文时原样返回中文（英文模式下会被自检脚本抓出来）。</summary>
        public static string T(string zh)
        {
            if (!_en || string.IsNullOrEmpty(zh)) return zh;
            if (_map == null) BuildMap();
            string en;
            return _map.TryGetValue(zh, out en) ? en : zh;
        }

        /// <summary>先取译文，再用 string.Format 填参数。</summary>
        public static string F(string zh, params object[] args)
        {
            string fmt = T(zh);
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch { return fmt; }
        }

        /// <summary>把「用 + 拼出来的整句」也翻译一下：整串命中就换，命不中保持原样。</summary>
        public static string Whole(string text)
        {
            if (!_en || string.IsNullOrEmpty(text)) return text;
            if (_map == null) BuildMap();
            string en;
            return _map.TryGetValue(text, out en) ? en : text;
        }

        /// <summary>
        /// 界面上「用 + 拼成两句」的静态文字：整串命不中翻译表时，
        /// 再按句子拆开逐句翻一遍（每句在表里有独立译文），否则会漏掉这种标签。
        /// </summary>
        public static string Static(string text)
        {
            if (!_en || string.IsNullOrEmpty(text)) return text;
            string whole = T(text);
            if (!ReferenceEquals(whole, text) && !string.Equals(whole, text, StringComparison.Ordinal)) return whole;

            if (text.IndexOf('。') < 0 && text.IndexOf('；') < 0) return text;

            var sb = new StringBuilder(text.Length);
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '。' && c != '；') continue;
                int end = i + 1;
                while (end < text.Length && text[end] == ' ') end++;
                sb.Append(T(text.Substring(start, end - start)));
                start = end;
                i = end - 1;
            }
            if (start < text.Length) sb.Append(T(text.Substring(start)));
            return sb.ToString();
        }

        private static void BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Pairs != null)
            {
                for (int i = 0; i + 1 < Pairs.Length; i += 2)
                {
                    string zh = Pairs[i];
                    string en = Pairs[i + 1];
                    if (!string.IsNullOrEmpty(zh) && !map.ContainsKey(zh)) map[zh] = en;
                }
            }
            _map = map;
        }

        // ------------------------------------------------------------------
        //  把可视树上的静态文字整棵换掉
        // ------------------------------------------------------------------

        /// <summary>
        /// 只翻静态文字（标签、按钮、分组框、选项卡、表头、菜单），
        /// 绝不碰输入框和表格内容——那些是用户数据。
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null || !_en) return;

            if (IsStaticText(root)) root.Text = Static(root.Text);

            var grid = root as DataGridView;
            if (grid != null)
            {
                foreach (DataGridViewColumn col in grid.Columns) col.HeaderText = T(col.HeaderText);
            }

            var strip = root as ToolStrip;
            if (strip != null) ApplyItems(strip.Items);

            var menu = root as MenuStrip;
            if (menu != null) ApplyItems(menu.Items);

            foreach (Control child in root.Controls) Apply(child);
        }

        public static void Apply(ContextMenuStrip menu)
        {
            if (menu == null || !_en) return;
            ApplyItems(menu.Items);
        }

        private static void ApplyItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            foreach (ToolStripItem item in items)
            {
                if (!string.IsNullOrEmpty(item.Text)) item.Text = T(item.Text);
                var dd = item as ToolStripDropDownItem;
                if (dd != null) ApplyItems(dd.DropDownItems);
            }
        }

        private static bool IsStaticText(Control c)
        {
            return c is Form || c is Label || c is Button || c is CheckBox || c is RadioButton
                   || c is GroupBox || c is TabPage || c is LinkLabel;
        }

        /// <summary>把某个控件自己的静态文字换掉（动态建出来的控件用）。</summary>
        public static void ApplyTo(Control c)
        {
            if (c == null || !_en) return;
            Apply(c);
        }
    }
}
