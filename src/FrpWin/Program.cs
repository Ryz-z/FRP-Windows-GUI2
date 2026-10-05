using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace FrpWin
{
    internal static class Program
    {
        private const string MutexName = "FrpWinManagerSingleInstance";
        private const string ActivateEventName = "FrpWinManagerActivateEvent";

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int STD_OUTPUT_HANDLE = -11;

        /// <summary>
        /// 保证命令行输出可见：只有当标准输出还没有被重定向/继承时才去附加父进程控制台，
        /// 否则会把调用方重定向的文件句柄覆盖掉。
        /// </summary>
        private static void EnsureConsole()
        {
            try
            {
                IntPtr h = GetStdHandle(STD_OUTPUT_HANDLE);
                if (h == IntPtr.Zero || h == INVALID_HANDLE_VALUE)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                }

                // 统一用 UTF-8 输出，否则中文日志在控制台里会显示成乱码
                try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); }
                catch { }
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // 界面语言必须先定下来，后面所有文字都从这里取
            try { Loc.Init(ConfigStore.PeekLanguage()); } catch { }

            // ---- 以 Windows 服务方式运行：FrpWin.exe --service frps|frpc ----
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
            {
                RunAsService(args[1]);
                return;
            }

            // ---- 命令行生成配置文件：FrpWin.exe --gen-config ----
            if (args != null && args.Length >= 1 &&
                string.Equals(args[0], "--gen-config", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(GenerateConfig());
                return;
            }

            // ---- 命令行前台运行：FrpWin.exe --run frps|frpc ----
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--run", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(RunForeground(args[1]));
                return;
            }

            // ---- 界面布局自检：FrpWin.exe --dump-layout [语言] [--expand-visitors] ----
            if (args != null && args.Length >= 1 &&
                string.Equals(args[0], "--dump-layout", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal)) Loc.Init(args[1]);
                bool expand = Array.IndexOf(args, "--expand-visitors") >= 0;
                Environment.Exit(DumpLayout(expand));
                return;
            }

            // ---- 界面渲染成图片：FrpWin.exe --screenshot <png> [标签页序号] [语言] ----
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase))
            {
                int tab = args.Length >= 3 ? int.Parse(args[2]) : 1;
                if (args.Length >= 4) Loc.Init(args[3]);
                if (args.Length >= 5 && string.Equals(args[4], "demo", StringComparison.OrdinalIgnoreCase))
                    ConfigStore.DemoOverride = BuildDemoSettings();
                Environment.Exit(TakeScreenshot(args[1], tab));
                return;
            }

            // ---- 读取 frps Dashboard 并把解析结果打印出来（自检 / 排错用）----
            //      FrpWin.exe --probe-frps <端口> [用户名] [密码]
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--probe-frps", StringComparison.OrdinalIgnoreCase))
            {
                int probePort;
                if (!int.TryParse(args[1], out probePort)) probePort = 0;
                if (args.Length >= 5) Loc.Init(args[4]);
                Environment.Exit(ProbeFrps(probePort,
                    args.Length >= 3 ? args[2] : "",
                    args.Length >= 4 ? args[3] : ""));
                return;
            }

            // ---- 解析现成的 frpc 配置并打印识别结果（自检 / 排错用）----
            //      FrpWin.exe --import-frpc <配置文件>
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--import-frpc", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(ImportFrpcDump(args[1]));
                return;
            }

            // ---- 子对话框布局自检：FrpWin.exe --dump-dialog clients|ports ----
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--dump-dialog", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 3) Loc.Init(args[2]);
                Environment.Exit(DumpDialog(args[1]));
                return;
            }

            // ---- 导入对话框布局自检：FrpWin.exe --dump-dialog import [语言] ----
            //      （复用上面的 --dump-dialog 分支，CreateDialogForTest 里认 import）

            // ---- 子对话框截图：FrpWin.exe --screenshot-dialog <png> clients|ports [语言] ----
            if (args != null && args.Length >= 3 &&
                string.Equals(args[0], "--screenshot-dialog", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 4) Loc.Init(args[3]);
                Environment.Exit(ScreenshotDialog(args[1], args[2]));
                return;
            }

            // ---- 界面文字自检：FrpWin.exe --dump-texts zh|en ----
            //      把所有静态文字按当前语言打印出来，自动化测试据此断言
            //      “英文模式下界面里不该再出现中文”。
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--dump-texts", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(DumpTexts(args[1]));
                return;
            }

            // ---- 图形界面（带单实例保护）----
            bool createdNew;
            var mutex = new Mutex(true, MutexName, out createdNew);
            bool ownIt = createdNew;

            if (!createdNew)
            {
                // 上一个实例可能是异常退出后遗弃了互斥体，这里尝试接管
                try { ownIt = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { ownIt = true; }
            }

            if (!ownIt)
            {
                // 已经有实例在运行：请它把窗口显示出来，然后自己立刻退出
                SignalExistingInstance();
                return;
            }

            // 这两个调用必须在创建任何窗口/控件之前完成
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName))
            {
                var form = new MainForm();

                var watcher = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            if (!activateEvent.WaitOne()) break;
                            if (form.IsDisposed || form.Disposing) break;
                            form.BeginInvoke(new Action(form.ActivateFromOtherInstance));
                        }
                        catch { break; }
                    }
                });
                watcher.IsBackground = true;
                watcher.Start();

                try
                {
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.F("程序发生未处理的错误：\r\n\r\n{0}", ex), Loc.T("FrpWin 错误"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            GC.KeepAlive(mutex);
        }

        /// <summary>
        /// 由 Windows 服务控制管理器启动。如果被用户直接双击 / 命令行运行，
        /// 这里给出友好提示并退出，而不是让 .NET 弹出一个会卡住进程的对话框。
        /// Environment.UserInteractive 为 true 就说明不是 services.exe 拉起来的。
        /// </summary>
        private static void RunAsService(string role)
        {
            if (Environment.UserInteractive)
            {
                string tip = Loc.T("FrpWin 服务宿主不能直接在命令行下运行。\r\n\r\n") +
                             Loc.T("请通过 Windows 服务控制管理器启动它（服务名：FrpWinServer / FrpWinClient），") +
                             Loc.T("或者直接双击 FrpWin.exe 使用图形界面来启停 frp。");

                EnsureConsole();
                Console.WriteLine(tip);
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "FrpWin-service.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + role + " : " + tip.Replace("\r\n", " ") + Environment.NewLine);
                }
                catch { }
                Environment.Exit(1);
                return;
            }

            try
            {
                ServiceBase.Run(new FrpService(role));
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "FrpWin-service.log"),
                        Loc.F("{0} {1} 服务启动失败: {2}{3}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), role, ex, Environment.NewLine));
                }
                catch { }
                Environment.Exit(1);
            }
        }

        private static void SignalExistingInstance()        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out ev))
                {
                    using (ev) { ev.Set(); }
                }
            }
            catch { }
        }

        /// <summary>
        /// 命令行前台运行 frps / frpc（不开图形界面），日志同时输出到控制台和服务日志文件。
        /// 与图形界面的“启动”按钮、以及 Windows 服务走的是同一套 FrpRunner 逻辑。
        /// frpc 支持配多个服务端，这里就会按连接各起一个进程。
        /// </summary>
        private static int RunForeground(string role)
        {
            bool isServer = string.Equals(role, "frps", StringComparison.OrdinalIgnoreCase);
            string tag = isServer ? "frps" : "frpc";
            string exe = isServer ? AppPaths.FrpsExe : AppPaths.FrpcExe;

            EnsureConsole();

            if (!File.Exists(exe))
            {
                Console.WriteLine(Loc.F("找不到程序文件：{0}", exe));
                return 1;
            }

            var runners = new List<FrpRunner>();
            var labels = new List<string>();

            try
            {
                if (isServer)
                {
                    if (!File.Exists(AppPaths.FrpsConfig)) RegenerateConfigs();
                    runners.Add(StartOne("frps", exe, AppPaths.FrpsConfig, "frps"));
                    labels.Add("frps");
                }
                else
                {
                    AppSettings settings = ConfigStore.Load();
                    string frpsPath;
                    List<string> cfgPaths;
                    ConfigStore.WriteToml(settings, out frpsPath, out cfgPaths);

                    for (int i = 0; i < settings.Client.Connections.Count; i++)
                    {
                        ClientConnection c = settings.Client.Connections[i];
                        if (!c.Enabled || i >= cfgPaths.Count) continue;
                        runners.Add(StartOne("frpc", exe, cfgPaths[i], c.Label));
                        labels.Add(c.Label);
                    }

                    if (runners.Count == 0)
                    {
                        Console.WriteLine(Loc.T("没有启用任何服务端连接，无事可做。"));
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.F("启动 {0} 失败：{1}", tag, ex.Message));
                foreach (var r in runners) { try { r.Dispose(); } catch { } }
                return 1;
            }

            for (int i = 0; i < runners.Count; i++)
            {
                Console.WriteLine(Loc.F("[{0}] 已启动 (PID {1})", labels[i], runners[i].ProcessId));
            }
            Console.WriteLine(Loc.F("按 Ctrl+C 结束。日志文件：{0}", AppPaths.ServiceLogFile(tag)));

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                foreach (var r in runners) { try { r.Stop(); } catch { } }
            };

            foreach (var r in runners)
            {
                using (r) { r.WaitForExit(-1); }
            }

            return 0;
        }

        private static FrpRunner StartOne(string role, string exe, string cfg, string label)
        {
            var runner = new FrpRunner();
            runner.Line += line => { try { Console.WriteLine("[" + label + "] " + line); } catch { } };
            runner.Start(role, exe, cfg, AppPaths.InstallDir, AppPaths.ServiceLogFile(role), label);
            Console.WriteLine(Loc.F("[{0}] 配置文件：{1}", label, cfg));
            return runner;
        }

        private static void RegenerateConfigs()
        {
            var settings = ConfigStore.Load();
            string p1;
            List<string> p2;
            ConfigStore.WriteToml(settings, out p1, out p2);
            Console.WriteLine(Loc.F("配置文件不存在，已自动生成：{0}", p1));
        }

        /// <summary>
        /// 界面布局自检：把窗口摆到屏幕外真实布局一次，然后打印所有控件的实际位置尺寸。
        /// 自动化测试据此判断按钮文字有没有被截断、按钮条有没有盖住表格。
        /// </summary>
        /// <summary>
        /// 把界面渲染成 PNG（窗口摆到屏幕外，不影响用户），用于人工/自动检查界面效果。
        /// </summary>
        private static int TakeScreenshot(string path, int tabIndex)
        {
            EnsureConsole();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new MainForm();
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                Application.DoEvents();

                form.SelectTabForTest(tabIndex);
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();

                using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                Console.WriteLine("已保存截图: " + path + "  (" + form.Width + "x" + form.Height + ")");

                form.Close();
                form.Dispose();
                Application.DoEvents();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.F("截图失败: {0}", ex));
                return 1;
            }
        }

        // =====================================================================
        //  自检 / 排错用的小工具
        // =====================================================================

        /// <summary>
        /// 读取本机 frps 的 Dashboard 管理接口，把解析结果按 “标签|字段…” 的形式打印出来。
        /// 自动化测试用它来验证 JSON 解析、客户端归类和端口清单生成是否正确。
        /// </summary>
        private static int ProbeFrps(int port, string user, string password)
        {
            EnsureConsole();
            var api = new FrpsApi("127.0.0.1", port, user, password, 4000);
            Console.WriteLine("BASE|" + api.BaseUrl);

            try
            {
                Console.WriteLine("SV|" + api.ProbeServerInfo());
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR|" + FrpsApi.Describe(ex).Replace("\r\n", " "));
                return 2;
            }

            List<FrpsClientInfo> clients;
            List<FrpsProxyInfo> proxies;
            try
            {
                clients = api.GetClients();
                proxies = api.GetProxies();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR|" + FrpsApi.Describe(ex).Replace("\r\n", " "));
                return 2;
            }

            foreach (FrpsClientInfo c in clients)
            {
                Console.WriteLine("CLIENT|" + c.Label + "|" + c.Hostname + "|" + c.ClientIP + "|" +
                                  c.Version + "|" + c.ClientID + "|" + (c.Online ? "online" : "offline"));
            }

            foreach (FrpsProxyInfo p in proxies)
            {
                Console.WriteLine("PROXY|" + p.Type + "|" + p.Name + "|" + p.ClientID + "|" +
                                  p.RemotePort + "|" + p.LocalPort + "|" + p.Status + "|" + p.Protocol);
            }

            foreach (FrpsClientInfo c in clients)
            {
                string note;
                List<PortEntry> entries = ClientPortPlan.Build(c, proxies, out note);
                foreach (PortEntry e in entries)
                {
                    Console.WriteLine("PORT|" + c.Label + "|" + e.PortValue + "|" + e.Protocol + "|" + e.ProxyName);
                    Console.WriteLine("PNOTE|" + c.Label + "|" + e.PortValue + "|" + e.Protocol + "|" + e.Note);
                }
                if (!string.IsNullOrEmpty(note)) Console.WriteLine("NOTE|" + c.Label + "|" + note);

                foreach (FirewallRule r in ClientPortPlan.ToRules(entries, true, true))
                {
                    Console.WriteLine("RULE|" + c.Label + "|" + r.RuleName + "|" + (r.Inbound ? "in" : "out"));
                    Console.WriteLine("ARGS|" + Firewall.BuildArguments(r));
                }
            }

            return 0;
        }

        /// <summary>
        /// 把一份 frpc 配置解析出来按行打印，供自动化测试断言「导入识别」是否正确。
        /// 输出格式（每行字段用 | 分隔）：
        ///     CONN|serverAddr|serverPort|hasServer|token|hasToken|tls|hasTls|logLevel|loginFailExit|adminPort
        ///     PROXY|name|type|localIP|localPort|remotePort|customDomains|subdomain|secretKey|enc|comp
        ///     PEXTRA|代理名|原始行
        ///     VISITOR|name|type|serverName|secretKey|bindAddr|bindPort
        ///     VEXTRA|访问者名|原始行
        ///     EXTRA|界面外参数条数
        ///     FILE|读到的文件
        /// </summary>
        private static int ImportFrpcDump(string path)
        {
            EnsureConsole();
            try
            {
                FrpcConfigFile.ImportResult r = FrpcConfigFile.Load(path);

                Console.WriteLine("CONN|" + r.ServerAddr + "|" + r.ServerPort + "|" + (r.HasServer ? "1" : "0")
                    + "|" + r.Token + "|" + (r.HasToken ? "1" : "0")
                    + "|" + (r.TlsEnable ? "true" : "false") + "|" + (r.HasTls ? "1" : "0")
                    + "|" + r.LogLevel + "|" + (r.LoginFailExit ? "true" : "false")
                    + "|" + r.AdminPort);

                foreach (ProxyItem p in r.Proxies)
                {
                    Console.WriteLine("PROXY|" + p.Name + "|" + p.Type + "|" + p.LocalIP + "|" + p.LocalPort
                        + "|" + p.RemotePort + "|" + p.CustomDomains + "|" + p.Subdomain + "|" + p.SecretKey
                        + "|" + (p.UseEncryption ? "true" : "false")
                        + "|" + (p.UseCompression ? "true" : "false"));
                    if (p.ExtraLines != null)
                        foreach (string e in p.ExtraLines) Console.WriteLine("PEXTRA|" + p.Name + "|" + e);
                }

                foreach (VisitorItem v in r.Visitors)
                {
                    Console.WriteLine("VISITOR|" + v.Name + "|" + v.Type + "|" + v.ServerName + "|"
                        + v.SecretKey + "|" + v.BindAddr + "|" + v.BindPort);
                    if (v.ExtraLines != null)
                        foreach (string e in v.ExtraLines) Console.WriteLine("VEXTRA|" + v.Name + "|" + e);
                }

                Console.WriteLine("EXTRA|" + r.ExtraKeyCount);
                foreach (string f in r.Files) Console.WriteLine("FILE|" + f);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR|" + ex.Message.Replace("\r\n", " "));
                return 2;
            }
        }

        /// <summary>给界面自检/截图造一个填了示例数据的对话框。</summary>
        private static Form CreateDialogForTest(string which)
        {
            // 「导入 frpc 配置」对话框：塞一段示例配置进去，好让截图和布局自检有内容
            if (string.Equals(which, "import", StringComparison.OrdinalIgnoreCase))
            {
                var imp = new FrpcImportForm("公司服务器");
                imp.LoadSampleForTest(
                    "serverAddr = \"203.0.113.10\"\r\n" +
                    "serverPort = 7000\r\n" +
                    "auth.token = \"ChangeMe_Strong_Token\"\r\n" +
                    "transport.tls.enable = true\r\n" +
                    "\r\n" +
                    "[[proxies]]\r\n" +
                    "name = \"rdp\"\r\n" +
                    "type = \"tcp\"\r\n" +
                    "localIP = \"127.0.0.1\"\r\n" +
                    "localPort = 3389\r\n" +
                    "remotePort = 13389\r\n" +
                    "\r\n" +
                    "[[proxies]]\r\n" +
                    "name = \"nas-http\"\r\n" +
                    "type = \"http\"\r\n" +
                    "localPort = 5000\r\n" +
                    "customDomains = [\"nas.example.com\"]\r\n" +
                    "\r\n" +
                    "[[visitors]]\r\n" +
                    "name = \"visitor-db\"\r\n" +
                    "type = \"stcp\"\r\n" +
                    "serverName = \"p2p-db\"\r\n" +
                    "bindPort = 13306\r\n");
                return imp;
            }

            List<FrpsClientInfo> clients;
            List<FrpsProxyInfo> proxies;
            ClientFirewallSample.Build(out clients, out proxies);

            if (string.Equals(which, "ports", StringComparison.OrdinalIgnoreCase))
            {
                string note;
                List<PortEntry> entries = ClientPortPlan.Build(clients[0], proxies, out note);
                return new ClientPortFirewallForm(
                    Loc.F("{0}（主机名 {1}，IP {2}）", clients[0].Label, clients[0].Hostname, clients[0].ClientIP),
                    entries, note, null, null);
            }

            var pick = new ClientPickForm("127.0.0.1", 7500, "admin", "admin", null, null);
            pick.LoadForTest(clients, proxies);
            pick.SelectFirstForTest();
            return pick;
        }

        private static int DumpDialog(string which)
        {
            EnsureConsole();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (Form form = CreateDialogForTest(which))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-4000, -4000);
                    form.Show();
                    Application.DoEvents();

                    Console.WriteLine(MainForm.DumpLayoutOf(form));

                    form.Close();
                    Application.DoEvents();
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.F("对话框布局自检失败: {0}", ex));
                return 1;
            }
        }

        private static int ScreenshotDialog(string path, string which)
        {
            EnsureConsole();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (Form form = CreateDialogForTest(which))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-4000, -4000);
                    form.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(300);
                    Application.DoEvents();

                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }

                    Console.WriteLine(Loc.F("已保存截图: {0}  ({1}x{2})", path, form.Width, form.Height));

                    form.Close();
                    Application.DoEvents();
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.F("对话框截图失败: {0}", ex));
                return 1;
            }
        }

        // =====================================================================
        //  界面文字自检：把所有静态文字按指定语言打印出来
        // =====================================================================

        private static int DumpTexts(string lang)
        {
            EnsureConsole();
            // "auto" 不动：Main 启动时已经按「界面设置 → 安装程序选择 → 系统语言」解析过了
            if (!string.Equals(lang, "auto", StringComparison.OrdinalIgnoreCase)) Loc.Init(lang);
            Console.WriteLine("LANG|" + Loc.Lang);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                foreach (string which in new[] { "main", "clients", "ports" })
                {
                    Form form = which == "main" ? new MainForm() : CreateDialogForTest(which);
                    using (form)
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-4000, -4000);
                        form.Show();
                        Application.DoEvents();
                        CollectTexts(form, which, form);

                    var main = form as MainForm;
                    if (main != null)
                    {
                        // 关于页的正文是拼出来的，托盘菜单不在控件树里，单独扫一遍
                        foreach (string line in MainForm.AboutText().Split('\n'))
                        {
                            string s = line.TrimEnd('\r');
                            if (s.Length > 0 && !string.IsNullOrWhiteSpace(s)) Console.WriteLine("ABOUT|" + s);
                        }
                        if (main.TrayMenuForTest != null) CollectItems(main.TrayMenuForTest.Items, which);
                    }
                        form.Close();
                        Application.DoEvents();
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR|" + ex.Message);
                return 1;
            }
        }

        private static void CollectTexts(Control root, string where, Control parent)
        {
            string name = string.IsNullOrEmpty(parent.Name) ? parent.GetType().Name : parent.Name;

            if (parent is Form || parent is Label || parent is Button || parent is CheckBox
                || parent is RadioButton || parent is GroupBox || parent is TabPage || parent is LinkLabel)
            {
                if (!string.IsNullOrEmpty(parent.Text)) Console.WriteLine("TEXT|" + where + "|" + name + "|" + parent.Text);
            }

            var grid = parent as DataGridView;
            if (grid != null)
            {
                foreach (DataGridViewColumn col in grid.Columns)
                    Console.WriteLine("HEAD|" + where + "|" + name + "|" + col.HeaderText);
            }

            var combo = parent as ComboBox;
            if (combo != null && !string.IsNullOrEmpty(combo.Name))
            {
                foreach (object item in combo.Items)
                    Console.WriteLine("ITEM|" + where + "|" + name + "|" + Convert.ToString(item));
            }

            var strip = parent as ToolStrip;
            if (strip != null) CollectItems(strip.Items, where);

            foreach (Control child in parent.Controls) CollectTexts(root, where, child);
        }

        private static void CollectItems(ToolStripItemCollection items, string where)
        {
            foreach (ToolStripItem item in items)
            {
                if (!string.IsNullOrEmpty(item.Text)) Console.WriteLine("MENU|" + where + "|" + item.Text);
                var dd = item as ToolStripDropDownItem;
                if (dd != null) CollectItems(dd.DropDownItems, where);
            }
        }

        /// <summary>截图用的一组示例设置（只影响 --screenshot ... demo，不碰磁盘）。</summary>
        private static AppSettings BuildDemoSettings()
        {
            var s = new AppSettings();
            s.Server.Token = "FhaFRZrLRUEZ88O2";
            s.Server.BindPort = 7000;
            s.Server.DashboardPort = 7500;

            var a = new ClientConnection
            {
                Label = Loc.T("公司服务器"), ServerAddr = "203.0.113.10", ServerPort = 7000,
                Token = "ChangeMe_Strong_Token", TlsEnable = true
            };
            a.Proxies.Add(new ProxyItem { Name = "rdp", Type = "tcp", LocalIP = "127.0.0.1", LocalPort = 3389, RemotePort = 13389 });
            a.Proxies.Add(new ProxyItem { Name = "web", Type = "http", LocalIP = "127.0.0.1", LocalPort = 80, CustomDomains = "www.example.com" });
            s.Client.Connections.Add(a);

            var b = new ClientConnection
            {
                Label = Loc.T("家里 NAS"), ServerAddr = "nas.example.com", ServerPort = 7001,
                Token = "Another_Token_Here", TlsEnable = true
            };
            b.Proxies.Add(new ProxyItem { Name = "nas-ssh", Type = "tcp", LocalIP = "10.0.0.9", LocalPort = 22, RemotePort = 6000 });
            b.Proxies.Add(new ProxyItem { Name = "nas-game", Type = "udp", LocalIP = "10.0.0.9", LocalPort = 7100, RemotePort = 7100 });
            s.Client.Connections.Add(b);

            var c = new ClientConnection
            {
                Label = Loc.T("测试服务器"), ServerAddr = "198.51.100.7", ServerPort = 7000,
                Token = "Third_Token", TlsEnable = false, Enabled = false
            };
            c.Proxies.Add(new ProxyItem { Name = "test", Type = "tcp", LocalIP = "127.0.0.1", LocalPort = 8080, RemotePort = 18080 });
            s.Client.Connections.Add(c);

            s.Client.Selected = 0;
            return s;
        }

        private static int DumpLayout(bool expandVisitors)
        {
            EnsureConsole();
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var form = new MainForm();
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                Application.DoEvents();

                if (expandVisitors)
                {
                    form.SetVisitorsExpandedForTest();
                    Application.DoEvents();
                }

                Console.WriteLine(form.DumpLayoutReport());
                Console.WriteLine(form.DumpVisitorState());

                form.Close();
                form.Dispose();
                Application.DoEvents();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Loc.F("布局自检失败: {0}", ex));
                return 1;
            }
        }

        private static int GenerateConfig()        {
            try
            {
                var settings = ConfigStore.Load();
                string frpsPath;
                List<string> frpcPaths;
                ConfigStore.WriteToml(settings, out frpsPath, out frpcPaths);
                ConfigStore.Save(settings);

                EnsureConsole();
                Console.WriteLine(Loc.F("已生成服务端配置: {0}", frpsPath));
                for (int i = 0; i < frpcPaths.Count; i++)
                {
                    string label = i < settings.Client.Connections.Count ? settings.Client.Connections[i].Label : "";
                    Console.WriteLine(Loc.F("已生成客户端配置: {0}（{1}）", frpcPaths[i], label));
                }
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    EnsureConsole();
                    Console.WriteLine(Loc.F("生成配置失败: {0}", ex.Message));
                }
                catch { }
                return 1;
            }
        }
    }
}
