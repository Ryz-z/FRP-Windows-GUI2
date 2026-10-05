using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace FrpWin
{
    /// <summary>frpc 代理条目。</summary>
    public class ProxyItem
    {
        public string Name { get; set; }
        public string Type { get; set; }          // tcp / udp / http / https / stcp / xtcp
        public string LocalIP { get; set; }
        public int LocalPort { get; set; }
        public int RemotePort { get; set; }
        public string CustomDomains { get; set; } // 逗号分隔（http/https）
        public string Subdomain { get; set; }     // http/https
        public string SecretKey { get; set; }     // stcp/xtcp
        public bool UseEncryption { get; set; }
        public bool UseCompression { get; set; }

        /// <summary>
        /// 从别人的 frpc.toml 导入时，界面上没有对应输入框的键
        /// （healthCheck、loadBalancer、transport.protocol、metadatas……）原样存在这里，
        /// 生成配置时再原样写回去，保证导入不丢参数。
        /// </summary>
        [XmlArrayItem("Line")]
        public List<string> ExtraLines { get; set; }

        public ProxyItem()
        {
            Name = "proxy1";
            Type = "tcp";
            LocalIP = "127.0.0.1";
            LocalPort = 80;
            RemotePort = 6000;
            CustomDomains = "";
            Subdomain = "";
            SecretKey = "";
            ExtraLines = new List<string>();
        }

        public ProxyItem Clone()
        {
            var p = (ProxyItem)MemberwiseClone();
            p.ExtraLines = ExtraLines == null ? new List<string>() : new List<string>(ExtraLines);
            return p;
        }
    }

    /// <summary>frpc 访问者条目（用于点对点 / 密钥穿透）。</summary>
    public class VisitorItem
    {
        public string Name { get; set; }
        public string Type { get; set; }          // stcp / xtcp
        public string ServerName { get; set; }
        public string SecretKey { get; set; }
        public string BindAddr { get; set; }
        public int BindPort { get; set; }

        /// <summary>同 ProxyItem.ExtraLines：导入时原样保留界面不认识的键。</summary>
        [XmlArrayItem("Line")]
        public List<string> ExtraLines { get; set; }

        public VisitorItem()
        {
            Name = "visitor1";
            Type = "stcp";
            ServerName = "";
            SecretKey = "";
            BindAddr = "127.0.0.1";
            BindPort = 6000;
            ExtraLines = new List<string>();
        }

        public VisitorItem Clone()
        {
            var v = (VisitorItem)MemberwiseClone();
            v.ExtraLines = ExtraLines == null ? new List<string>() : new List<string>(ExtraLines);
            return v;
        }
    }

    /// <summary>
    /// 一个服务端连接。
    ///
    /// frp 的 frpc 一个进程只能连一个服务端（ClientCommonConfig 里只有一组
    /// serverAddr / serverPort），所以「一个客户端连多个服务端」的做法是：
    /// 每个连接生成一份独立的 frpc-*.toml，再各起一个 frpc 进程。
    /// 这个类就是其中一份连接配置。
    /// </summary>
    public class ClientConnection
    {
        /// <summary>内部标识，用来把界面上的连接和正在跑的 frpc 进程对上号，不写进 frp 配置。</summary>
        public string Id { get; set; }

        /// <summary>界面上显示的名字，比如「公司服务器」「家里 NAS」。</summary>
        public string Label { get; set; }

        /// <summary>不勾选就不启动这个连接。</summary>
        public bool Enabled { get; set; }

        public string ServerAddr { get; set; }
        public int ServerPort { get; set; }
        public string Token { get; set; }
        public bool TlsEnable { get; set; }
        public string LogLevel { get; set; }
        public bool LoginFailExit { get; set; }
        public int AdminPort { get; set; }        // frpc 自带管理界面，0 表示关闭
        public string AdminUser { get; set; }
        public string AdminPassword { get; set; }

        [XmlArrayItem("Proxy")]
        public List<ProxyItem> Proxies { get; set; }

        [XmlArrayItem("Visitor")]
        public List<VisitorItem> Visitors { get; set; }

        public ClientConnection()
        {
            Id = Guid.NewGuid().ToString("N");
            Label = "";
            Enabled = true;
            ServerAddr = "127.0.0.1";
            ServerPort = 7000;
            Token = "";
            TlsEnable = true;
            LogLevel = "info";
            LoginFailExit = false;
            AdminPort = 0;
            AdminUser = "admin";
            AdminPassword = "admin";
            Proxies = new List<ProxyItem>();
            Visitors = new List<VisitorItem>();
        }

        public ClientConnection Clone()
        {
            var c = (ClientConnection)MemberwiseClone();
            c.Proxies = new List<ProxyItem>();
            foreach (var p in Proxies) c.Proxies.Add(p.Clone());
            c.Visitors = new List<VisitorItem>();
            foreach (var v in Visitors) c.Visitors.Add(v.Clone());
            return c;
        }

        /// <summary>把反序列化出来的对象补全，避免老配置里缺字段导致空引用。</summary>
        public void EnsureValid()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Proxies == null) Proxies = new List<ProxyItem>();
            if (Visitors == null) Visitors = new List<VisitorItem>();
            if (LogLevel == null) LogLevel = "info";
            if (AdminUser == null) AdminUser = "admin";
            if (AdminPassword == null) AdminPassword = "admin";
            if (Token == null) Token = "";
            if (ServerAddr == null) ServerAddr = "";
            if (Label == null) Label = "";

            // v1.0.0 之前保存的设置里没有 ExtraLines 字段，反序列化出来是 null
            foreach (ProxyItem p in Proxies)
            {
                if (p == null) continue;
                if (p.ExtraLines == null) p.ExtraLines = new List<string>();
                if (p.Name == null) p.Name = "";
                if (p.Type == null) p.Type = "tcp";
                if (p.LocalIP == null) p.LocalIP = "";
                if (p.CustomDomains == null) p.CustomDomains = "";
                if (p.Subdomain == null) p.Subdomain = "";
                if (p.SecretKey == null) p.SecretKey = "";
            }
            for (int i = Proxies.Count - 1; i >= 0; i--)
                if (Proxies[i] == null) Proxies.RemoveAt(i);

            foreach (VisitorItem v in Visitors)
            {
                if (v == null) continue;
                if (v.ExtraLines == null) v.ExtraLines = new List<string>();
                if (v.Name == null) v.Name = "";
                if (v.Type == null) v.Type = "stcp";
                if (v.ServerName == null) v.ServerName = "";
                if (v.SecretKey == null) v.SecretKey = "";
                if (v.BindAddr == null) v.BindAddr = "127.0.0.1";
            }
            for (int i = Visitors.Count - 1; i >= 0; i--)
                if (Visitors[i] == null) Visitors.RemoveAt(i);
        }
    }

    /// <summary>服务端（frps）设置。</summary>
    public class ServerSettings
    {
        public string BindAddr { get; set; }
        public int BindPort { get; set; }
        public int KcpBindPort { get; set; }      // 0 表示关闭
        public int VhostHttpPort { get; set; }    // 0 表示关闭
        public int VhostHttpsPort { get; set; }   // 0 表示关闭
        public string SubDomainHost { get; set; }
        public string Token { get; set; }
        public int DashboardPort { get; set; }    // 0 表示关闭 Dashboard
        public string DashboardUser { get; set; }
        public string DashboardPassword { get; set; }
        public string LogLevel { get; set; }
        public int LogMaxDays { get; set; }

        public ServerSettings()
        {
            BindAddr = "0.0.0.0";
            BindPort = 7000;
            KcpBindPort = 0;
            VhostHttpPort = 0;
            VhostHttpsPort = 0;
            SubDomainHost = "";
            Token = "";
            DashboardPort = 7500;
            DashboardUser = "admin";
            DashboardPassword = "admin";
            LogLevel = "info";
            LogMaxDays = 3;
        }
    }

    /// <summary>客户端（frpc）设置：一组服务端连接 + 界面上当前在编辑哪一个。</summary>
    public class ClientSettings
    {
        /// <summary>界面上当前选中的连接下标（纯界面状态）。</summary>
        public int Selected { get; set; }

        [XmlArrayItem("Connection")]
        public List<ClientConnection> Connections { get; set; }

        public ClientSettings()
        {
            Selected = 0;
            Connections = new List<ClientConnection>();
        }

        /// <summary>取当前选中的连接；越界回落到第一个，一个都没有就补一个空的。</summary>
        public ClientConnection Current()
        {
            if (Connections == null) Connections = new List<ClientConnection>();
            if (Connections.Count == 0) Connections.Add(new ClientConnection());
            if (Selected < 0 || Selected >= Connections.Count) Selected = 0;
            return Connections[Selected];
        }
    }

    /// <summary>全部界面设置，序列化到 ui-settings.xml。</summary>
    [XmlRoot("FrpWinSettings")]
    public class AppSettings
    {
        /// <summary>界面语言：auto / zh / en。</summary>
        public string Language { get; set; }

        public ServerSettings Server { get; set; }
        public ClientSettings Client { get; set; }

        public AppSettings()
        {
            Language = Loc.Auto;
            Server = new ServerSettings();
            Client = new ClientSettings();
        }
    }

    // ======================================================================
    //  旧版本（第三版及以前）的设置格式，只用来把老配置迁移成多连接格式
    // ======================================================================

    [XmlRoot("FrpWinSettings")]
    public class LegacyAppSettings
    {
        public ServerSettings Server { get; set; }
        public LegacyClientSettings Client { get; set; }
    }

    /// <summary>第三版及以前的单服务端客户端设置。</summary>
    public class LegacyClientSettings
    {
        public string ServerAddr { get; set; }
        public int ServerPort { get; set; }
        public string Token { get; set; }
        public bool TlsEnable { get; set; }
        public string LogLevel { get; set; }
        public bool LoginFailExit { get; set; }
        public int AdminPort { get; set; }
        public string AdminUser { get; set; }
        public string AdminPassword { get; set; }

        [XmlArrayItem("Proxy")]
        public List<ProxyItem> Proxies { get; set; }

        [XmlArrayItem("Visitor")]
        public List<VisitorItem> Visitors { get; set; }
    }
}
