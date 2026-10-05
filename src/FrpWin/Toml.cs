using System;
using System.Collections.Generic;
using System.Text;

namespace FrpWin
{
    /// <summary>根据界面设置生成 frp 官方 TOML 配置文件（适配 frp v0.71+）。</summary>
    internal static class Toml
    {
        private static string Q(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Header(string role)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ============================================================");
            sb.AppendLine(Loc.F("#  FrpWin 管理器自动生成的 {0} 配置文件", role));
            sb.AppendLine(Loc.F("#  生成时间: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine(Loc.T("#  注意: 在 FrpWin 图形界面里点\"保存并启动\"会重新生成并覆盖本文件；"));
            sb.AppendLine(Loc.T("#        想手工调整参数，请改界面里的对应设置，或直接使用 frp 官方命令行。"));
            sb.AppendLine("#  完整参数说明: https://github.com/fatedier/frp");
            sb.AppendLine("# ============================================================");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string BuildFrps(ServerSettings s)
        {
            var sb = new StringBuilder();
            sb.Append(Header(Loc.T("服务端 frps")));

            sb.AppendLine("bindAddr = " + Q(s.BindAddr));
            sb.AppendLine("bindPort = " + s.BindPort);
            if (s.KcpBindPort > 0) sb.AppendLine("kcpBindPort = " + s.KcpBindPort);
            if (s.VhostHttpPort > 0) sb.AppendLine("vhostHTTPPort = " + s.VhostHttpPort);
            if (s.VhostHttpsPort > 0) sb.AppendLine("vhostHTTPSPort = " + s.VhostHttpsPort);
            if (!string.IsNullOrWhiteSpace(s.SubDomainHost)) sb.AppendLine("subDomainHost = " + Q(s.SubDomainHost.Trim()));
            sb.AppendLine();

            if (!string.IsNullOrEmpty(s.Token))
            {
                sb.AppendLine(Loc.T("# 客户端必须使用相同的 token 才能连接"));
                sb.AppendLine("auth.method = \"token\"");
                sb.AppendLine("auth.token = " + Q(s.Token));
                sb.AppendLine();
            }

            if (s.DashboardPort > 0)
            {
                sb.AppendLine(Loc.F("# 管理面板 (Dashboard)，浏览器访问 http://<服务器IP>:{0}", s.DashboardPort));
                sb.AppendLine("webServer.addr = \"0.0.0.0\"");
                sb.AppendLine("webServer.port = " + s.DashboardPort);
                sb.AppendLine("webServer.user = " + Q(s.DashboardUser));
                sb.AppendLine("webServer.password = " + Q(s.DashboardPassword));
                sb.AppendLine();
            }

            sb.AppendLine("log.to = \"console\"");
            sb.AppendLine("log.level = " + Q(NormalizeLevel(s.LogLevel)));
            sb.AppendLine("log.maxDays = " + Math.Max(1, s.LogMaxDays));
            sb.AppendLine("log.disablePrintColor = true");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string BuildFrpc(ClientConnection c, string label)
        {
            var sb = new StringBuilder();
            sb.Append(Header(Loc.T("客户端 frpc") + (string.IsNullOrEmpty(label) ? "" : " - " + label)));

            sb.AppendLine("serverAddr = " + Q(c.ServerAddr));
            sb.AppendLine("serverPort = " + c.ServerPort);
            sb.AppendLine("loginFailExit = " + (c.LoginFailExit ? "true" : "false"));
            sb.AppendLine();

            if (!string.IsNullOrEmpty(c.Token))
            {
                sb.AppendLine("auth.method = \"token\"");
                sb.AppendLine("auth.token = " + Q(c.Token));
                sb.AppendLine();
            }

            sb.AppendLine(Loc.T("# 与服务端之间的连接启用 TLS 加密"));
            sb.AppendLine("transport.tls.enable = " + (c.TlsEnable ? "true" : "false"));
            sb.AppendLine();

            if (c.AdminPort > 0)
            {
                sb.AppendLine(Loc.F("# frpc 自带的管理界面 http://127.0.0.1:{0}", c.AdminPort));
                sb.AppendLine("webServer.addr = \"127.0.0.1\"");
                sb.AppendLine("webServer.port = " + c.AdminPort);
                sb.AppendLine("webServer.user = " + Q(c.AdminUser));
                sb.AppendLine("webServer.password = " + Q(c.AdminPassword));
                sb.AppendLine();
            }

            sb.AppendLine("log.to = \"console\"");
            sb.AppendLine("log.level = " + Q(NormalizeLevel(c.LogLevel)));
            sb.AppendLine("log.maxDays = 3");
            sb.AppendLine("log.disablePrintColor = true");
            sb.AppendLine();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (c.Proxies != null)
            {
                foreach (var p in c.Proxies)
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                    string name = p.Name.Trim();
                    if (!used.Add(name)) continue;  // 跳过重名，避免 frpc 启动失败

                    sb.AppendLine("[[proxies]]");
                    sb.AppendLine("name = " + Q(name));
                    sb.AppendLine("type = " + Q(p.Type));
                    if (!string.IsNullOrWhiteSpace(p.LocalIP)) sb.AppendLine("localIP = " + Q(p.LocalIP.Trim()));
                    sb.AppendLine("localPort = " + p.LocalPort);

                    string type = (p.Type ?? "").ToLowerInvariant();
                    if (type == "tcp" || type == "udp")
                    {
                        sb.AppendLine("remotePort = " + p.RemotePort);
                    }
                    else if (type == "http" || type == "https")
                    {
                        var domains = SplitDomains(p.CustomDomains);
                        if (domains.Count > 0)
                        {
                            var items = new List<string>();
                            foreach (var d in domains) items.Add(Q(d));
                            sb.AppendLine("customDomains = [" + string.Join(", ", items) + "]");
                        }
                        if (!string.IsNullOrWhiteSpace(p.Subdomain)) sb.AppendLine("subdomain = " + Q(p.Subdomain.Trim()));
                    }
                    else if (type == "stcp" || type == "xtcp" || type == "sudp")
                    {
                        if (!string.IsNullOrEmpty(p.SecretKey)) sb.AppendLine("secretKey = " + Q(p.SecretKey));
                    }

                    if (p.UseEncryption) sb.AppendLine("transport.useEncryption = true");
                    if (p.UseCompression) sb.AppendLine("transport.useCompression = true");

                    // 从别人的 frpc.toml 导入时保留下来的键（healthCheck、metadatas……）
                    // 原样写回去，保证导入不丢参数
                    if (p.ExtraLines != null)
                    {
                        foreach (string extra in p.ExtraLines)
                        {
                            if (!string.IsNullOrWhiteSpace(extra)) sb.AppendLine(extra.Trim());
                        }
                    }
                    sb.AppendLine();
                }
            }

            if (c.Visitors != null)
            {
                foreach (var v in c.Visitors)
                {
                    if (v == null || string.IsNullOrWhiteSpace(v.Name) || string.IsNullOrWhiteSpace(v.ServerName)) continue;
                    sb.AppendLine("[[visitors]]");
                    sb.AppendLine("name = " + Q(v.Name.Trim()));
                    sb.AppendLine("type = " + Q(v.Type));
                    sb.AppendLine("serverName = " + Q(v.ServerName.Trim()));
                    if (!string.IsNullOrEmpty(v.SecretKey)) sb.AppendLine("secretKey = " + Q(v.SecretKey));
                    sb.AppendLine("bindAddr = " + Q(string.IsNullOrWhiteSpace(v.BindAddr) ? "127.0.0.1" : v.BindAddr.Trim()));
                    sb.AppendLine("bindPort = " + v.BindPort);
                    if (v.ExtraLines != null)
                    {
                        foreach (string extra in v.ExtraLines)
                        {
                            if (!string.IsNullOrWhiteSpace(extra)) sb.AppendLine(extra.Trim());
                        }
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static List<string> SplitDomains(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            foreach (var part in raw.Split(new[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string d = part.Trim();
                if (d.Length > 0) list.Add(d);
            }
            return list;
        }

        private static string NormalizeLevel(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return "info";
            string l = level.Trim().ToLowerInvariant();
            switch (l)
            {
                case "trace":
                case "debug":
                case "info":
                case "warn":
                case "error":
                    return l;
                default:
                    return "info";
            }
        }
    }
}
