using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace FrpWin
{
    /// <summary>通过 sc.exe 管理 FrpWin 的 Windows 服务。</summary>
    internal static class ServiceManager
    {
        public const string ServerServiceName = "FrpWinServer";
        public const string ClientServiceName = "FrpWinClient";

        public static string ServiceNameFor(string role)
        {
            return string.Equals(role, "frps", StringComparison.OrdinalIgnoreCase) ? ServerServiceName : ClientServiceName;
        }

        public static bool IsAdministrator
        {
            get
            {
                try
                {
                    using (var id = WindowsIdentity.GetCurrent())
                    {
                        var principal = new WindowsPrincipal(id);
                        return principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch { return false; }
            }
        }

        private sealed class ScResult
        {
            public int ExitCode;
            public string Output = "";
            public bool Ok { get { return ExitCode == 0; } }
        }

        private static ScResult Sc(params string[] args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(a);
            }

            var psi = new ProcessStartInfo("sc.exe", sb.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var r = new ScResult();
            try
            {
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    r.ExitCode = p.ExitCode;
                    r.Output = (o + "\n" + e).Trim();
                }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Output = ex.Message;
            }
            return r;
        }

        private static string Q(string s) { return "\"" + s + "\""; }

        public static bool IsInstalled(string serviceName)
        {
            var r = Sc("query", Q(serviceName));
            return r.Ok;
        }

        /// <summary>返回服务状态，如 RUNNING / STOPPED / 未安装。</summary>
        public static string QueryState(string serviceName)
        {
            var r = Sc("query", Q(serviceName));
            if (!r.Ok) return Loc.T("未安装");

            foreach (var raw in r.Output.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    int idx = line.IndexOf(':');
                    if (idx >= 0)
                    {
                        string rest = line.Substring(idx + 1).Trim();
                        int sp = rest.IndexOf(' ');
                        if (sp > 0) rest = rest.Substring(0, sp);
                        return rest.Trim();
                    }
                }
            }
            return Loc.T("未知");
        }

        public static string Install(string role, out bool ok)
        {
            string serviceName = ServiceNameFor(role);
            string display = string.Equals(role, "frps", StringComparison.OrdinalIgnoreCase)
                ? Loc.T("FrpWin 服务端 (frps)")
                : Loc.T("FrpWin 客户端 (frpc)");

            if (IsInstalled(serviceName))
            {
                // 先卸载旧的，保证 binPath 是最新的
                Sc("stop", Q(serviceName));
                System.Threading.Thread.Sleep(600);
                Sc("delete", Q(serviceName));
                System.Threading.Thread.Sleep(600);
            }

            string binPath = "\\\"" + AppPaths.SelfExe + "\\\" --service " + role;

            var r = Sc(
                "create", Q(serviceName),
                "binPath=", Q(binPath),
                "start=", "auto",
                "DisplayName=", Q(display));

            if (!r.Ok)
            {
                ok = false;
                return r.Output;
            }

            Sc("description", Q(serviceName), Q(Loc.T("FrpWin 内网穿透服务，由 FrpWin 管理器托管，开机自动运行。")));
            Sc("failure", Q(serviceName), "reset=", "86400",
                "actions=", "restart/5000/restart/10000/restart/30000");

            var st = Sc("start", Q(serviceName));
            ok = true;
            return st.Ok
                ? Loc.T("服务已安装并启动。")
                : Loc.F("服务已安装，但启动失败：\n{0}", st.Output);
        }

        public static string Uninstall(string role, out bool ok)
        {
            string serviceName = ServiceNameFor(role);
            if (!IsInstalled(serviceName))
            {
                ok = true;
                return Loc.T("服务未安装，无需卸载。");
            }

            Sc("stop", Q(serviceName));
            System.Threading.Thread.Sleep(800);

            var r = Sc("delete", Q(serviceName));
            ok = r.Ok;
            return r.Ok ? Loc.T("服务已停止并删除。") : r.Output;
        }

        public static string Restart(string serviceName)
        {
            Sc("stop", Q(serviceName));
            System.Threading.Thread.Sleep(800);
            var r = Sc("start", Q(serviceName));
            return r.Output;
        }
    }
}
