using System;
using System.IO;
using System.Reflection;

namespace FrpWin
{
    /// <summary>集中管理程序目录、数据目录与各类文件路径。</summary>
    internal static class AppPaths
    {
        /// <summary>程序安装目录（FrpWin.exe 所在目录）。</summary>
        public static string InstallDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); }
        }

        public static string SelfExe
        {
            get { return Assembly.GetEntryAssembly().Location; }
        }

        public static string FrpsExe { get { return Path.Combine(InstallDir, "frps.exe"); } }
        public static string FrpcExe { get { return Path.Combine(InstallDir, "frpc.exe"); } }

        private static string _dataDir;

        /// <summary>数据目录：优先 C:\ProgramData\FrpWin，不可写时回退到当前用户目录。</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;

                string machine = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FrpWin");
                if (TryEnsureDir(machine)) { _dataDir = machine; return _dataDir; }

                string user = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrpWin");
                TryEnsureDir(user);
                _dataDir = user;
                return _dataDir;
            }
        }

        private static bool TryEnsureDir(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                // 真正验证可写性
                string probe = Path.Combine(path, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "ui-settings.xml"); } }
        public static string FrpsConfig { get { return Path.Combine(DataDir, "frps.toml"); } }

        /// <summary>第一个服务端连接的配置文件，沿用老名字，命令行习惯不受影响。</summary>
        public static string FrpcConfig { get { return FrpcConfigFor(0); } }

        /// <summary>
        /// 第 index 个服务端连接的配置文件（index 从 0 开始）：
        /// 第 1 个沿用 frpc.toml，后面依次是 frpc-2.toml、frpc-3.toml……
        /// </summary>
        public static string FrpcConfigFor(int index)
        {
            if (index <= 0) return Path.Combine(DataDir, "frpc.toml");
            return Path.Combine(DataDir, "frpc-" + (index + 1) + ".toml");
        }

        public static string LogDir
        {
            get
            {
                string d = Path.Combine(DataDir, "logs");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string ServiceLogFile(string role) { return Path.Combine(LogDir, role + ".service.log"); }
    }
}
