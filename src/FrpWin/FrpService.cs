using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace FrpWin
{
    /// <summary>
    /// Windows 服务宿主：以 Windows 服务身份运行时，由它把 frps.exe / frpc.exe
    /// 作为子进程拉起并守护，输出写入 ProgramData\FrpWin\logs。
    ///
    /// 客户端支持配多个服务端，这里就对应地起多个 frpc 进程（一个连接一个进程）。
    /// </summary>
    internal sealed class FrpService : ServiceBase
    {
        private readonly string _role;
        private readonly List<FrpRunner> _runners = new List<FrpRunner>();

        public FrpService(string role)
        {
            _role = string.Equals(role, "frps", StringComparison.OrdinalIgnoreCase) ? "frps" : "frpc";
            ServiceName = ServiceManager.ServiceNameFor(_role);
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            if (_role == "frps") StartServer();
            else StartClients();
        }

        // ------------------------------------------------------------------

        private void StartServer()
        {
            string exe = AppPaths.FrpsExe;
            string cfg = AppPaths.FrpsConfig;

            if (!File.Exists(exe))
                throw new FileNotFoundException(Loc.T("服务无法启动，缺少程序文件：") + exe);

            if (!File.Exists(cfg)) EnsureConfigs();

            var runner = new FrpRunner();
            runner.Start("frps", exe, cfg, AppPaths.InstallDir, AppPaths.ServiceLogFile("frps"));
            _runners.Add(runner);

            EventLog.WriteEntry(ServiceName,
                Loc.T("已启动 ") + Path.GetFileName(exe) + Loc.T("，配置：") + cfg, EventLogEntryType.Information);
        }

        private void StartClients()
        {
            string exe = AppPaths.FrpcExe;
            if (!File.Exists(exe))
                throw new FileNotFoundException(Loc.T("服务无法启动，缺少程序文件：") + exe);

            AppSettings settings = ConfigStore.Load();

            string frpsPath;
            List<string> cfgPaths;
            ConfigStore.WriteToml(settings, out frpsPath, out cfgPaths);

            int started = 0;
            for (int i = 0; i < settings.Client.Connections.Count; i++)
            {
                ClientConnection c = settings.Client.Connections[i];
                if (!c.Enabled) continue;
                if (i >= cfgPaths.Count) break;

                var runner = new FrpRunner();
                runner.Start("frpc", exe, cfgPaths[i], AppPaths.InstallDir,
                             AppPaths.ServiceLogFile("frpc"), c.Label);
                _runners.Add(runner);
                started++;

                EventLog.WriteEntry(ServiceName,
                    Loc.F("已启动 {0} → {1}:{2}，配置：{3}", c.Label, c.ServerAddr, c.ServerPort, cfgPaths[i]),
                    EventLogEntryType.Information);
            }

            if (started == 0)
                throw new InvalidOperationException(Loc.T("没有启用任何服务端连接，服务无事可做。请在界面里勾选「启用这个连接」。"));
        }

        /// <summary>配置文件丢失时，用界面保存过的设置自动补一份，避免服务起不来。</summary>
        private void EnsureConfigs()
        {
            try
            {
                AppSettings settings = ConfigStore.Load();
                string p1;
                List<string> p2;
                ConfigStore.WriteToml(settings, out p1, out p2);
                EventLog.WriteEntry(ServiceName,
                    Loc.T("配置文件不存在，已根据界面设置自动生成：") + p1, EventLogEntryType.Warning);
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException(
                    Loc.T("服务无法启动，缺少配置文件且自动生成失败：") + AppPaths.FrpsConfig, ex);
            }
        }

        protected override void OnStop()
        {
            foreach (FrpRunner r in _runners)
            {
                try { r.Stop(); r.Dispose(); } catch { }
            }
            _runners.Clear();
            EventLog.WriteEntry(ServiceName, Loc.T("服务已停止。"), EventLogEntryType.Information);
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }
    }
}
