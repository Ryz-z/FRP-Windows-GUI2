using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FrpWin
{
    /// <summary>界面设置的读写，以及把设置渲染成 frp 的 TOML 配置文件。</summary>
    internal static class ConfigStore
    {
        /// <summary>只读一下界面语言，用来在加载完整设置之前先把 Loc 初始化好。</summary>
        public static string PeekLanguage()
        {
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) return null;
                var ser = new XmlSerializer(typeof(AppSettings));
                using (var fs = File.OpenRead(AppPaths.SettingsFile))
                {
                    var s = ser.Deserialize(fs) as AppSettings;
                    return s == null ? null : s.Language;
                }
            }
            catch { return null; }
        }

        /// <summary>截图自检用：不为空时 Load() 直接返回它，不读磁盘。</summary>
        internal static AppSettings DemoOverride;

        public static AppSettings Load()
        {
            if (DemoOverride != null) return DemoOverride;

            AppSettings s = null;
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var ser = new XmlSerializer(typeof(AppSettings));
                    using (var fs = File.OpenRead(AppPaths.SettingsFile))
                    {
                        s = ser.Deserialize(fs) as AppSettings;
                    }
                }
            }
            catch { s = null; /* 设置损坏时回退到默认值 */ }

            if (s == null) return CreateDefault();

            if (s.Server == null) s.Server = new ServerSettings();
            if (s.Client == null) s.Client = new ClientSettings();
            if (s.Client.Connections == null) s.Client.Connections = new List<ClientConnection>();

            // 第三版及以前只有一个服务端连接，把老配置迁移成一条连接
            if (s.Client.Connections.Count == 0)
            {
                ClientConnection migrated = MigrateLegacyClient();
                s.Client.Connections.Add(migrated ?? new ClientConnection());
                s.Client.Selected = 0;
            }

            for (int i = 0; i < s.Client.Connections.Count; i++)
            {
                s.Client.Connections[i].EnsureValid();
                if (string.IsNullOrEmpty(s.Client.Connections[i].Label))
                    s.Client.Connections[i].Label = DefaultLabel(i);
            }
            if (s.Client.Selected < 0 || s.Client.Selected >= s.Client.Connections.Count) s.Client.Selected = 0;

            if (string.IsNullOrEmpty(s.Language)) s.Language = Loc.Auto;
            if (s.Server.LogLevel == null) s.Server.LogLevel = "info";
            if (s.Server.BindAddr == null) s.Server.BindAddr = "0.0.0.0";
            if (s.Server.Token == null) s.Server.Token = "";
            if (s.Server.DashboardUser == null) s.Server.DashboardUser = "admin";
            if (s.Server.DashboardPassword == null) s.Server.DashboardPassword = "admin";

            return s;
        }

        /// <summary>连接的默认显示名，例如「服务器 1」。</summary>
        public static string DefaultLabel(int index)
        {
            return Loc.F("服务器 {0}", index + 1);
        }

        /// <summary>把第三版及以前的单服务端设置读成一条连接。</summary>
        private static ClientConnection MigrateLegacyClient()
        {
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) return null;

                var ser = new XmlSerializer(typeof(LegacyAppSettings));
                LegacyAppSettings old;
                using (var fs = File.OpenRead(AppPaths.SettingsFile))
                {
                    old = ser.Deserialize(fs) as LegacyAppSettings;
                }
                if (old == null || old.Client == null) return null;

                var c = new ClientConnection
                {
                    Label = DefaultLabel(0),
                    Enabled = true,
                    ServerAddr = old.Client.ServerAddr ?? "127.0.0.1",
                    ServerPort = old.Client.ServerPort > 0 ? old.Client.ServerPort : 7000,
                    Token = old.Client.Token ?? "",
                    TlsEnable = old.Client.TlsEnable,
                    LogLevel = string.IsNullOrEmpty(old.Client.LogLevel) ? "info" : old.Client.LogLevel,
                    LoginFailExit = old.Client.LoginFailExit,
                    AdminPort = old.Client.AdminPort,
                    AdminUser = string.IsNullOrEmpty(old.Client.AdminUser) ? "admin" : old.Client.AdminUser,
                    AdminPassword = old.Client.AdminPassword ?? "admin"
                };
                if (old.Client.Proxies != null) c.Proxies.AddRange(old.Client.Proxies);
                if (old.Client.Visitors != null) c.Visitors.AddRange(old.Client.Visitors);
                c.EnsureValid();
                return c;
            }
            catch { return null; }
        }

        private static AppSettings CreateDefault()
        {
            var s = new AppSettings();
            s.Server.Token = RandomToken();

            var c = new ClientConnection
            {
                Label = DefaultLabel(0),
                ServerAddr = "127.0.0.1",
                ServerPort = 7000,
                Token = s.Server.Token
            };
            c.Proxies.Add(new ProxyItem
            {
                Name = "rdp",
                Type = "tcp",
                LocalIP = "127.0.0.1",
                LocalPort = 3389,
                RemotePort = 13389
            });
            s.Client.Connections.Add(c);
            return s;
        }

        public static string RandomToken()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            var sb = new StringBuilder(16);
            for (int i = 0; i < 16; i++) sb.Append(chars[rnd.Next(chars.Length)]);
            return sb.ToString();
        }

        public static void Save(AppSettings s)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            var ser = new XmlSerializer(typeof(AppSettings));
            using (var fs = File.Create(AppPaths.SettingsFile))
            {
                ser.Serialize(fs, s);
            }
        }

        /// <summary>
        /// 写出 frps.toml 和每个服务端连接各自的 frpc-*.toml。
        /// 连接数变少时，把多出来的历史配置文件删掉，免得被误当成有效配置。
        /// </summary>
        public static void WriteToml(AppSettings s, out string frpsPath, out List<string> frpcPaths)
        {
            Directory.CreateDirectory(AppPaths.DataDir);

            frpsPath = AppPaths.FrpsConfig;
            File.WriteAllText(frpsPath, Toml.BuildFrps(s.Server), new UTF8Encoding(false));

            var list = (s.Client != null && s.Client.Connections != null)
                ? s.Client.Connections : new List<ClientConnection>();

            frpcPaths = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                string path = AppPaths.FrpcConfigFor(i);
                File.WriteAllText(path, Toml.BuildFrpc(list[i], list[i].Label), new UTF8Encoding(false));
                frpcPaths.Add(path);
            }

            for (int i = list.Count; i < list.Count + 16; i++)
            {
                string stale = AppPaths.FrpcConfigFor(i);
                if (!File.Exists(stale)) break;
                try { File.Delete(stale); } catch { }
            }
        }

        /// <summary>兼容用：只要第一个连接的配置文件路径。</summary>
        public static void WriteToml(AppSettings s, out string frpsPath, out string frpcPath)
        {
            List<string> all;
            WriteToml(s, out frpsPath, out all);
            frpcPath = all.Count > 0 ? all[0] : AppPaths.FrpcConfig;
        }
    }
}
