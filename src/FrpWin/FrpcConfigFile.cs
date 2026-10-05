using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FrpWin
{
    /// <summary>
    /// 解析一份现成的 frpc 配置文件（frp v0.52+ 的 TOML，也兼容更老的 INI 格式），
    /// 把里面的「服务端连接」和「代理规则 / 访问者」提取成界面上的对象。
    ///
    /// 目的：从别的机器上拷一份 frpc.toml 过来，不用照着一条一条重新敲。
    ///
    /// 设计取舍：
    ///  · 不认识的键（healthCheck、loadBalancer、transport.protocol、metadatas……）
    ///    一律原样保留在 ProxyItem.ExtraLines 里，保存时再原样写回去，
    ///    这样导入不会悄悄丢配置。
    ///  · includes 会被展开（相对路径按配置文件所在目录 + 可执行文件目录解析），
    ///    带环路保护。
    /// </summary>
    internal static class FrpcConfigFile
    {
        // =====================================================================
        //  导入结果
        // =====================================================================
        internal sealed class ImportResult
        {
            /// <summary>配置文件里写的 serverAddr / server_addr，没有就是空串。</summary>
            public string ServerAddr = "";
            public int ServerPort;
            /// <summary>配置文件里是否真的写了服务端地址（可能只有代理规则，连接信息在别处）。</summary>
            public bool HasServer;

            public string Token = "";
            public bool HasToken;
            public bool TlsEnable;
            public bool HasTls;
            public string LogLevel = "";
            public bool LoginFailExit;
            public int AdminPort;
            public string AdminUser = "admin";
            public string AdminPassword = "admin";

            public readonly List<ProxyItem> Proxies = new List<ProxyItem>();
            public readonly List<VisitorItem> Visitors = new List<VisitorItem>();

            /// <summary>展开过的所有文件（含 includes），用于在界面上告诉用户读了哪些文件。</summary>
            public readonly List<string> Files = new List<string>();

            /// <summary>被保留下来、界面不认识的键的条数，便于提示用户“有额外参数已原样保留”。</summary>
            public int ExtraKeyCount;

            public bool HasAnything
            {
                get { return HasServer || Proxies.Count > 0 || Visitors.Count > 0; }
            }
        }

        // =====================================================================
        //  入口
        // =====================================================================

        /// <summary>从文件读入并解析（会展开 includes）。</summary>
        public static ImportResult Load(string path)
        {
            var result = new ImportResult();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ParseFile(path, result, visited, 0);
            return result;
        }

        /// <summary>直接解析一段配置文本（用户在界面上粘贴的内容）。</summary>
        public static ImportResult ParseText(string text, string baseDir)
        {
            var result = new ImportResult();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ParseContent(text, baseDir, result, visited, 0);
            return result;
        }

        // =====================================================================
        //  解析
        // =====================================================================

        private static void ParseFile(string path, ImportResult result, HashSet<string> visited, int depth)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (depth > 5) return;

            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }

            if (!visited.Add(full)) return;
            if (!File.Exists(full)) return;

            string text;
            try { text = ReadTextSmart(full); }
            catch { return; }

            result.Files.Add(full);
            ParseContent(text, Path.GetDirectoryName(full), result, visited, depth);
        }

        /// <summary>
        /// 配置文件可能是 UTF-8（带或不带 BOM），也可能是老编辑器存的 GBK。
        /// 先按 UTF-8 严格解码，失败再退回系统默认编码，避免中文注释变成乱码。
        /// </summary>
        private static string ReadTextSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch
            {
                try { return Encoding.Default.GetString(bytes); }
                catch { return new UTF8Encoding(false).GetString(bytes); }
            }
        }

        private sealed class Section
        {
            public string Kind = "common";   // common | proxy | visitor | other
            public bool OldStyle;            // 老 INI：代理段落的名字由用户随便起，靠 type 键认出来
            public bool RealHeader;          // 是配置文件里真的写了表头，而不是文件开头的隐式段
            public bool Finalized;           // 这个段落的类型已经定下来了
            public ProxyItem Proxy;
            public VisitorItem Visitor;
            public readonly List<string> Extra = new List<string>();
        }

        private static void ParseContent(string text, string baseDir, ImportResult result,
                                         HashSet<string> visited, int depth)
        {
            if (string.IsNullOrEmpty(text)) return;

            var sections = new List<Section>();
            var cur = new Section();
            sections.Add(cur);

            string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string rawLine in rawLines)
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                // ---------- 表头 ----------
                if (line[0] == '[')
                {
                    string header = line.TrimStart('[').TrimEnd(']').Trim();
                    bool arrayOfTables = line.StartsWith("[[", StringComparison.Ordinal);

                    if (arrayOfTables && string.Equals(header, "proxies", StringComparison.OrdinalIgnoreCase))
                    {
                        cur = new Section { Kind = "proxy", Proxy = NewProxy() };
                    }
                    else if (arrayOfTables && string.Equals(header, "visitors", StringComparison.OrdinalIgnoreCase))
                    {
                        cur = new Section { Kind = "visitor", Visitor = NewVisitor() };
                    }
                    else if (arrayOfTables)
                    {
                        // 其它 [[xxx]] 段落（比如 [[proxies.healthCheck]]）跟着当前条目走
                        cur = new Section { Kind = cur.Kind, Proxy = cur.Proxy, Visitor = cur.Visitor };
                    }
                    else if (!arrayOfTables && !header.Contains(".") &&
                             string.Equals(header, "common", StringComparison.OrdinalIgnoreCase))
                    {
                        cur = new Section { Kind = "common", RealHeader = true };
                    }
                    else if (!arrayOfTables && !header.Contains("."))
                    {
                        // 老 INI 格式的写法：[common] 后面跟着一堆段落，
                        // 段落名字由用户随便起（[web] / [ssh] / [rdp-3389]……），
                        // 要靠段落里的 type 键才知道它到底是代理还是访问者。
                        cur = new Section { Kind = "common", OldStyle = true, RealHeader = true };
                    }
                    else
                    {
                        cur = new Section { Kind = "other" };
                    }
                    sections.Add(cur);
                    continue;
                }

                // ---------- 键 = 值 ----------
                int eq;
                string key = SplitKeyValue(line, out eq);
                if (key == null) continue;
                string val = line.Substring(eq + 1).Trim();

                // 老 INI 的段落在读到第一个键时才能定性：有 type 就是代理/访问者段
                if (cur.RealHeader && !cur.Finalized && cur.Kind == "common")
                {
                    cur.Finalized = true;
                    if (string.Equals(Normalize(key), "type", StringComparison.Ordinal))
                    {
                        cur.OldStyle = true;
                        cur.Proxy = NewProxy();
                        cur.Kind = "proxy";
                    }
                }

                Apply(result, cur, key, val, baseDir, visited, depth);
            }

            // 段落收尾
            foreach (Section s in sections)
            {
                if (s.Proxy != null) FinalizeOldProxy(result, s.Proxy);
                if (s.Visitor != null && string.IsNullOrWhiteSpace(s.Visitor.Name))
                    s.Visitor.Name = "visitor-" + (result.Visitors.Count + 1);

                if (s.Kind == "proxy" && s.Proxy != null && !string.IsNullOrWhiteSpace(s.Proxy.Name))
                    result.Proxies.Add(s.Proxy);
                else if (s.Kind == "visitor" && s.Visitor != null && !string.IsNullOrWhiteSpace(s.Visitor.Name))
                    result.Visitors.Add(s.Visitor);
                if (s.Proxy != null) result.ExtraKeyCount += s.Extra.Count;
                else if (s.Visitor != null) result.ExtraKeyCount += s.Extra.Count;
            }
        }

        /// <summary>
        /// 老 INI 的段落没有 name 键（名字就是段落名，比如 [ssh]），
        /// 所以段落解析完后补一个不重名的名字。
        /// </summary>
        private static void FinalizeOldProxy(ImportResult result, ProxyItem p)
        {
            if (p == null || !string.IsNullOrWhiteSpace(p.Name)) return;
            if (string.IsNullOrWhiteSpace(p.Type)) { p.Type = "tcp"; return; }

            for (int i = 1; i <= 999; i++)
            {
                string cand = "auto-" + p.Type + i;
                bool used = false;
                foreach (ProxyItem q in result.Proxies)
                {
                    if (string.Equals(q.Name, cand, StringComparison.OrdinalIgnoreCase)) { used = true; break; }
                }
                if (!used) { p.Name = cand; return; }
            }
            p.Name = "auto-" + p.Type;
        }

        private static ProxyItem NewProxy()
        {
            return new ProxyItem { Name = "", Type = "tcp", LocalIP = "", LocalPort = 0, RemotePort = 0,
                                   CustomDomains = "", Subdomain = "", SecretKey = "" };
        }

        private static VisitorItem NewVisitor()
        {
            return new VisitorItem { Name = "", Type = "stcp", ServerName = "", SecretKey = "",
                                     BindAddr = "", BindPort = 0 };
        }

        // =====================================================================
        //  把一条 key = value 应用到当前段落
        // =====================================================================
        private static void Apply(ImportResult result, Section s, string key, string val,
                                  string baseDir, HashSet<string> visited, int depth)
        {
            string k = Normalize(key);

            // ---------------- 老 INI 的代理段落 ----------------
            if (s.Kind == "proxy" && s.OldStyle && s.Proxy != null)
            {
                ProxyItem p = s.Proxy;
                switch (k)
                {
                    case "name": p.Name = Unquote(val); return;
                    case "localip": p.LocalIP = Unquote(val); return;
                    case "localport": p.LocalPort = Int(val); return;
                    case "remoteport": p.RemotePort = Int(val); return;
                    case "customdomains": p.CustomDomains = ArrayText(val); return;
                    case "subdomain": p.Subdomain = Unquote(val); return;
                    case "sk": case "secretkey": p.SecretKey = Unquote(val); return;
                    case "useencryption": p.UseEncryption = Bool(val); return;
                    case "usecompression": p.UseCompression = Bool(val); return;
                    case "type": p.Type = Unquote(val); return;
                    case "role": return;                         // visitor 角色，界面里由访问者表承担
                    case "serveraddr": result.ServerAddr = Unquote(val); result.HasServer = true; return;
                    case "serverport": result.ServerPort = Int(val); return;
                    case "privilegetoken": case "authtoken": case "token":
                        result.Token = Unquote(val); result.HasToken = true; return;
                    case "tlsenable": result.TlsEnable = Bool(val); result.HasTls = true; return;
                    case "loglevel": case "logfile": case "logmaxdays": case "logto": return;
                    case "adminaddr": case "adminport": return;
                    case "protocol": case "heartbeatinterval": case "heartbeattimeout":
                    case "dialservertimeout": case "poolcount": case "user":
                    case "dnsserver": case "start": return;
                    default: s.Extra.Add(key + " = " + val); return;
                }
            }

            // ---------------- 代理条目 ----------------
            if (s.Kind == "proxy" && s.Proxy != null)
            {
                ProxyItem p = s.Proxy;

                // transport.* 下的两个常用开关，界面上有对应勾选框
                if (k == "transport.useencryption") { p.UseEncryption = Bool(val); return; }
                if (k == "transport.usecompression") { p.UseCompression = Bool(val); return; }
                if (k == "healthcheck.type" || k.StartsWith("healthcheck.", StringComparison.Ordinal) ||
                    k.StartsWith("loadbalancer.", StringComparison.Ordinal) ||
                    k.StartsWith("transport.", StringComparison.Ordinal) ||
                    k.StartsWith("metadatas", StringComparison.Ordinal) ||
                    k.StartsWith("annotations.", StringComparison.Ordinal))
                {
                    p.ExtraLines.Add(key + " = " + val);
                    return;
                }

                switch (k)
                {
                    case "name": p.Name = Unquote(val); return;
                    case "type": p.Type = Unquote(val); return;
                    case "localip": p.LocalIP = Unquote(val); return;
                    case "localport": p.LocalPort = Int(val); return;
                    case "remoteport": p.RemotePort = Int(val); return;
                    case "customdomains": p.CustomDomains = ArrayText(val); return;
                    case "subdomain": p.Subdomain = Unquote(val); return;
                    case "secretkey": p.SecretKey = Unquote(val); return;
                    case "transport.protocol": p.ExtraLines.Add(key + " = " + val); return;
                    default: p.ExtraLines.Add(key + " = " + val); return;
                }
            }

            // ---------------- 访问者条目 ----------------
            if (s.Kind == "visitor" && s.Visitor != null)
            {
                VisitorItem v = s.Visitor;
                switch (k)
                {
                    case "name": v.Name = Unquote(val); return;
                    case "type": v.Type = Unquote(val); return;
                    case "servername": v.ServerName = Unquote(val); return;
                    case "secretkey": v.SecretKey = Unquote(val); return;
                    case "bindaddr": v.BindAddr = Unquote(val); return;
                    case "bindport": v.BindPort = Int(val); return;
                    default: v.ExtraLines.Add(key + " = " + val); return;
                }
            }

            // ---------------- 公共段 ----------------
            switch (k)
            {
                case "serveraddr": result.ServerAddr = Unquote(val); result.HasServer = true; return;
                case "serverport": result.ServerPort = Int(val); return;

                case "auth.token": case "auth.privilegetoken": case "token":
                    result.Token = Unquote(val); result.HasToken = true; return;
                case "auth.method": case "authenticationmethod": return;

                case "transport.tls.enable": case "tls.enable": case "tlsenable":
                    result.TlsEnable = Bool(val); result.HasTls = true; return;
                case "transport.tlscertfile": case "transport.tlskeyfile":
                case "transport.tlstrustedcas": case "transport.tlsservername":
                case "transport.heartbeatinterval": case "transport.heartbeattimeout":
                case "transport.dialservertimeout": case "transport.protocol":
                case "transport.poolcount": case "transport.tcpMux":
                    return;

                case "loglevel": case "log.level": result.LogLevel = Unquote(val); return;
                case "logfile": case "log.to": case "logfilepath": case "logmaxdays":
                case "log.maxdays": case "log.disableprintcolor": case "log.disablelogcolor":
                    return;

                case "loginfailexit": result.LoginFailExit = Bool(val); return;

                case "adminaddr": case "webserver.addr": return;
                case "adminport": case "webserver.port": result.AdminPort = Int(val); return;
                case "adminuser": case "webserver.user": result.AdminUser = Unquote(val); return;
                case "adminpwd": case "adminpassword": case "webserver.password":
                    result.AdminPassword = Unquote(val); return;

                case "includes":
                    foreach (string inc in ParseIncludes(val))
                    {
                        string resolved = ResolveInclude(inc, baseDir);
                        if (resolved != null) ParseFile(resolved, result, visited, depth + 1);
                    }
                    return;

                case "user": case "dnsserver": case "start": case "udppacketsize":
                case "metadatas": case "metadatas.token": case "natholestunserver":
                    return;

                default:
                    // 不认识但可能有用的公共参数：不保留（这是「连接设置」页，
                    // 界面里没有对应输入框），只统计数量提醒用户
                    result.ExtraKeyCount++;
                    return;
            }
        }

        // =====================================================================
        //  小工具
        // =====================================================================

        /// <summary>去掉行尾注释，但引号里的 # 不算注释。</summary>
        private static string StripComment(string line)
        {
            if (line == null) return "";
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && (inSingle || inDouble)) { i++; continue; }
                if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
                if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
                if (c == '#' && !inSingle && !inDouble) return line.Substring(0, i);
            }
            return line;
        }

        /// <summary>取 key = value 里的 key，eq 返回等号位置；不是赋值行返回 null。</summary>
        private static string SplitKeyValue(string line, out int eq)
        {
            eq = -1;
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && (inSingle || inDouble)) { i++; continue; }
                if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
                if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
                if (c == '=' && !inSingle && !inDouble) { eq = i; break; }
            }
            if (eq <= 0) return null;
            string key = line.Substring(0, eq).Trim();
            if (key.Length == 0) return null;
            return key;
        }

        /// <summary>
        /// 把键统一成便于比较的形式：去掉引号、下划线、连字符，全部小写；
        /// 但保留 . 分隔（transport.tls.enable 和 transport_tls_enable 都能对上）。
        /// </summary>
        private static string Normalize(string key)
        {
            if (key == null) return "";
            var sb = new StringBuilder(key.Length);
            foreach (char c in key.Trim().Trim('"', '\''))
            {
                if (c == '.' ) { sb.Append('.'); continue; }
                if (c == '_' || c == '-' || c == ' ') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static string Unquote(string val)
        {
            if (val == null) return "";
            val = val.Trim();
            if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                return Unescape(val.Substring(1, val.Length - 2));
            if (val.Length >= 2 && val[0] == '\'' && val[val.Length - 1] == '\'')
                return val.Substring(1, val.Length - 2);
            return val;
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                                break;
                            }
                        }
                        sb.Append('u');
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        private static int Int(string val)
        {
            int v;
            if (int.TryParse(Unquote(val), out v)) return v;
            return 0;
        }

        private static bool Bool(string val)
        {
            string v = Unquote(val).Trim().ToLowerInvariant();
            return v == "true" || v == "1" || v == "yes" || v == "on";
        }

        /// <summary>["a.com","b.com"] 或 "a.com,b.com" 都转成界面用的逗号分隔串。</summary>
        private static string ArrayText(string val)
        {
            string v = val.Trim();
            if (v.StartsWith("[", StringComparison.Ordinal))
            {
                v = v.TrimStart('[').TrimEnd(']');
                var parts = new List<string>();
                foreach (string piece in v.Split(','))
                {
                    string one = Unquote(piece.Trim());
                    if (one.Length > 0) parts.Add(one);
                }
                return string.Join(", ", parts.ToArray());
            }
            return Unquote(v);
        }

        private static IEnumerable<string> ParseIncludes(string val)
        {
            string v = val.Trim();
            if (!v.StartsWith("[", StringComparison.Ordinal))
            {
                string one = Unquote(v);
                if (one.Length > 0) yield return one;
                yield break;
            }
            v = v.TrimStart('[').TrimEnd(']');
            foreach (string piece in v.Split(','))
            {
                string one = Unquote(piece.Trim());
                if (one.Length > 0) yield return one;
            }
        }

        private static string ResolveInclude(string inc, string baseDir)
        {
            try
            {
                if (Path.IsPathRooted(inc)) return File.Exists(inc) ? inc : null;

                if (!string.IsNullOrEmpty(baseDir))
                {
                    string p = Path.Combine(baseDir, inc);
                    if (File.Exists(p)) return p;
                }
                string exeDir = AppPaths.InstallDir;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string p = Path.Combine(exeDir, inc);
                    if (File.Exists(p)) return p;
                }
                return null;
            }
            catch { return null; }
        }
    }
}
