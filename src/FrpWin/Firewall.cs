using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FrpWin
{
    /// <summary>待放行的一条端口（“② 选择端口”对话框里的一行）。</summary>
    internal sealed class PortEntry
    {
        public bool Allow = true;
        public string PortText = "";
        public string Protocol = "TCP";     // TCP / UDP
        public string ProxyName = "";
        public string Note = "";

        public int PortValue
        {
            get
            {
                int v;
                return int.TryParse((PortText ?? "").Trim(), out v) ? v : 0;
            }
        }

        public bool IsValidPort
        {
            get { int v = PortValue; return v >= 1 && v <= 65535; }
        }

        public bool IsUdp
        {
            get { return string.Equals((Protocol ?? "").Trim(), "UDP", StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>一条真正要下发的 Windows 防火墙规则。</summary>
    internal sealed class FirewallRule
    {
        public readonly int Port;
        public readonly string Protocol;    // TCP / UDP
        public readonly bool Inbound;

        public FirewallRule(int port, string protocol, bool inbound)
        {
            Port = port;
            Protocol = string.Equals((protocol ?? "").Trim(), "UDP", StringComparison.OrdinalIgnoreCase) ? "UDP" : "TCP";
            Inbound = inbound;
        }

        /// <summary>
        /// 规则名只用 ASCII，避免 netsh 在中文代码页下把参数搞乱；
        /// 前缀固定为 "FrpWin Client"，反复执行时先删同名再加，不会越点越多。
        /// </summary>
        public string RuleName
        {
            get { return "FrpWin Client " + Port + " " + Protocol + " " + (Inbound ? "IN" : "OUT"); }
        }

        public string DisplayName
        {
            get { return Protocol + " " + Port + "（" + (Inbound ? "进站" : "出站") + "）"; }
        }
    }

    internal sealed class FirewallApplyResult
    {
        public FirewallRule Rule;
        public bool Ok;
        public string Error = "";
    }

    /// <summary>
    /// 通过 netsh advfirewall 下发防火墙规则。
    ///
    /// 方向语义和 Windows 自带的“新建入站/出站规则 → 端口”向导保持一致：
    /// 选“进站”就放行外部访问本机的这个端口；选“出站”就放行本机使用这个端口
    /// 对外发起的连接。两者都用 localport，和系统向导里的“特定本地端口”一致。
    /// </summary>
    internal static class Firewall
    {
        /// <summary>
        /// 组装 netsh 的参数（不含 "netsh" 本身）。
        /// 单独抽出来是为了能被自检打印出来、直接喂给真实 netsh 做语法校验：
        /// netsh 在检查管理员权限之前就会先校验参数，所以即使没有管理员权限，
        /// 也能从“参数是否合法”这一点上验证这条命令是对的。
        /// </summary>
        public static string BuildArguments(FirewallRule rule)
        {
            return "advfirewall firewall add rule name=\"" + rule.RuleName + "\"" +
                   " dir=" + (rule.Inbound ? "in" : "out") +
                   " action=allow" +
                   " protocol=" + rule.Protocol +
                   " localport=" + rule.Port +
                   " profile=any" +
                   " enable=yes";
        }

        /// <summary>下发一批规则。每条都先删同名规则，保证重复点击不会堆积。</summary>
        public static List<FirewallApplyResult> Apply(IList<FirewallRule> rules, Action<string> log)
        {
            var results = new List<FirewallApplyResult>(rules == null ? 0 : rules.Count);
            if (rules == null) return results;

            foreach (FirewallRule rule in rules)
            {
                var result = new FirewallApplyResult { Rule = rule };

                string quoted = "\"" + rule.RuleName + "\"";
                string ignoredOut, ignoredErr;
                RunNetsh("advfirewall firewall delete rule name=" + quoted, out ignoredOut, out ignoredErr);

                string args = BuildArguments(rule);

                string stdout, stderr;
                int code = RunNetsh(args, out stdout, out stderr);

                result.Ok = code == 0;
                if (!result.Ok) result.Error = FirstMeaningfulLine(stderr, stdout, code);

                if (log != null)
                {
                    log(result.Ok
                        ? Loc.F("防火墙：已放行 {0}  规则名 {1}", rule.DisplayName, rule.RuleName)
                        : Loc.F("防火墙：放行失败 {0}  规则名 {1}  {2}", rule.DisplayName, rule.RuleName, result.Error));
                }

                results.Add(result);
            }

            return results;
        }

        private static string FirstMeaningfulLine(string stderr, string stdout, int code)
        {
            foreach (string block in new[] { stderr, stdout })
            {
                if (string.IsNullOrEmpty(block)) continue;
                foreach (string raw in block.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("netsh", StringComparison.OrdinalIgnoreCase)) continue;
                    return line;
                }
            }
            return Loc.F("netsh 退出码 {0}", code);
        }

        private static int RunNetsh(string arguments, out string stdout, out string stderr)
        {
            stdout = "";
            stderr = "";

            var psi = new ProcessStartInfo("netsh.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // netsh 的控制台输出跟随系统 ANSI 代码页（本程序清单里声明了 UTF-8），
                // 用 Encoding.Default 才能保证中文提示不乱码。
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            try
            {
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        stderr = Loc.T("netsh 执行超时");
                        return 1;
                    }
                    stdout = outTask.Result ?? "";
                    stderr = errTask.Result ?? "";
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return 1;
            }
        }
    }

    /// <summary>把“某个客户端”翻译成“它需要放行哪些端口”。</summary>
    internal static class ClientPortPlan
    {
        /// <summary>按 clientID（新版 frp 每个客户端会话唯一）把代理归到客户端名下。</summary>
        public static List<FrpsProxyInfo> ProxiesOf(FrpsClientInfo client, IList<FrpsProxyInfo> all)
        {
            var list = new List<FrpsProxyInfo>();
            if (client == null || all == null) return list;

            foreach (FrpsProxyInfo p in all)
            {
                if (string.IsNullOrEmpty(p.ClientID)) continue;
                if (string.Equals(p.ClientID, client.ClientID, StringComparison.OrdinalIgnoreCase)) list.Add(p);
            }

            // 兜底：老版本 frp 可能没有 clientID，这时用 user 匹配
            if (list.Count == 0 && !string.IsNullOrEmpty(client.User))
            {
                foreach (FrpsProxyInfo p in all)
                {
                    if (string.Equals(p.User, client.User, StringComparison.Ordinal)) list.Add(p);
                }
            }

            return list;
        }

        /// <summary>
        /// 生成端口清单。
        /// 只有 tcp / udp 代理会在服务端占用独立端口；http / https / tcpmux 走的是
        /// 服务端的虚拟主机端口（用“放行防火墙端口”按钮统一处理），stcp / xtcp / sudp 是
        /// 点对点穿透，不占服务端端口。这些会写进 note 里告诉用户。
        /// </summary>
        public static List<PortEntry> Build(FrpsClientInfo client, IList<FrpsProxyInfo> all, out string note)
        {
            var entries = new List<PortEntry>();
            var skipped = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int offlineCount = 0;

            foreach (FrpsProxyInfo p in ProxiesOf(client, all))
            {
                if (p.HasServerPort)
                {
                    string key = p.Protocol + "/" + p.RemotePort;
                    if (!seen.Add(key)) continue;           // 同名端口去重（TCP 8080 和 UDP 8080 是两条）

                    if (!p.IsOnline) offlineCount++;

                    entries.Add(new PortEntry
                    {
                        Allow = true,
                        PortText = p.RemotePort.ToString(),
                        Protocol = p.Protocol,
                        ProxyName = p.Name,
                        Note = Loc.T(p.IsOnline ? "在线" : "离线") + " · " + p.TargetText()
                    });
                }
                else
                {
                    skipped.Add(p.Name + "（" + p.Type + "）");
                }
            }

            entries.Sort((a, b) =>
            {
                int c = a.PortValue.CompareTo(b.PortValue);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Protocol, b.Protocol);
            });

            var sb = new StringBuilder();
            if (skipped.Count > 0)
            {
                sb.Append(Loc.F("另有 {0} 个代理不占用服务端独立端口：{1}。",
                    skipped.Count, string.Join("、", skipped.ToArray())));
                sb.Append(Loc.T("http/https 走服务端的虚拟主机端口，可用“放行防火墙端口”统一放行；"));
                sb.Append(Loc.T("stcp/xtcp/sudp 是点对点穿透，不需要在服务端开端口。"));
            }
            if (offlineCount > 0)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(Loc.T("标“离线”的端口来自客户端上一次连接的记录——它现在没在线，放行了暂时也用不上，"));
                sb.Append(Loc.T("但不影响其它端口。"));
            }
            note = sb.ToString();

            return entries;
        }

        /// <summary>把勾选的行变成真正的防火墙规则。</summary>
        public static List<FirewallRule> ToRules(IList<PortEntry> entries, bool inbound, bool outbound)
        {
            var rules = new List<FirewallRule>();
            if (entries == null) return rules;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PortEntry e in entries)
            {
                if (e == null || !e.Allow || !e.IsValidPort) continue;
                string proto = e.IsUdp ? "UDP" : "TCP";

                if (inbound)
                {
                    string k = proto + "/" + e.PortValue + "/in";
                    if (seen.Add(k)) rules.Add(new FirewallRule(e.PortValue, proto, true));
                }
                if (outbound)
                {
                    string k = proto + "/" + e.PortValue + "/out";
                    if (seen.Add(k)) rules.Add(new FirewallRule(e.PortValue, proto, false));
                }
            }

            return rules;
        }
    }
}
