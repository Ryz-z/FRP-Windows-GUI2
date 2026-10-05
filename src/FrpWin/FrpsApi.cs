using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace FrpWin
{
    /// <summary>frps 管理接口 /api/clients 返回的一条“已连接客户端”。</summary>
    internal sealed class FrpsClientInfo
    {
        public string Key = "";
        public string User = "";
        public string ClientID = "";
        public string RunID = "";
        public string Version = "";
        public string Hostname = "";
        public string ClientIP = "";
        public bool Online;

        /// <summary>界面上显示的名字：优先用 frpc 里配置的 user，其次主机名，最后用客户端 ID 前 8 位。</summary>
        public string Label
        {
            get
            {
                if (!string.IsNullOrEmpty(User)) return User;
                if (!string.IsNullOrEmpty(Hostname)) return Hostname;
                if (!string.IsNullOrEmpty(ClientID)) return Shorten(ClientID);
                return "未命名客户端";
            }
        }

        public static string Shorten(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            return id.Length <= 8 ? id : id.Substring(0, 8);
        }
    }

    /// <summary>frps 管理接口 /api/proxy/&lt;type&gt; 返回的一条代理规则。</summary>
    internal sealed class FrpsProxyInfo
    {
        public string Name = "";
        public string Type = "";        // tcp / udp / http / https / tcpmux / stcp / xtcp / sudp
        public string User = "";
        public string ClientID = "";
        public string Status = "";      // online / offline
        public string LocalIP = "";
        public string Subdomain = "";
        public int RemotePort;
        public int LocalPort;
        public readonly List<string> Domains = new List<string>();

        /// <summary>这条代理占用的传输层协议；http/https/tcpmux 走的是服务端的虚拟主机端口，这里返回空。</summary>
        public string Protocol
        {
            get
            {
                string t = (Type ?? "").ToLowerInvariant();
                if (t == "tcp") return "TCP";
                if (t == "udp" || t == "sudp") return "UDP";
                return "";
            }
        }

        /// <summary>是否在服务端占用了一个独立端口（tcp / udp 才有 remotePort）。</summary>
        public bool HasServerPort
        {
            get { return Protocol.Length > 0 && RemotePort > 0 && RemotePort <= 65535; }
        }

        public bool IsOnline
        {
            get { return string.Equals(Status, "online", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// 这条代理的“说明”文本（界面上端口表格的“说明”列）。
        ///
        /// 注意：frps 实际上并不知道客户端把流量转发到了内网哪个端口——本地地址是
        /// 客户端自己的事，`localPort` 在真实接口里基本不会出现（实测 frp v0.71
        /// 的 /api/proxy/tcp 里只有 localIP 的默认值，没有 localPort）。
        /// 所以拿不到时要说清楚，不能让用户以为那里是空的 / 转发没配好。
        /// </summary>
        public string TargetText()
        {
            if (LocalPort > 0)
                return Loc.F("转发到内网 {0}:{1}", string.IsNullOrEmpty(LocalIP) ? "127.0.0.1" : LocalIP, LocalPort);
            if (Domains.Count > 0) return Loc.F("域名 {0}", string.Join(",", Domains.ToArray()));
            if (!string.IsNullOrEmpty(Subdomain)) return Loc.F("子域名 {0}", Subdomain);
            return Loc.T("转发到该客户端的内网服务");
        }
    }

    /// <summary>
    /// frps Dashboard（管理面板）HTTP 客户端。
    ///
    /// frp v0.71 的服务端管理接口（server/api_router.go）：
    ///   GET /api/serverinfo      服务端信息与版本
    ///   GET /api/clients         已连接 / 曾连接的客户端列表
    ///   GET /api/proxy/{type}    某一类代理的实时状态与配置（conf.remotePort 就是占用端口）
    ///
    /// 这些接口由 webServer.user / webServer.password 做 HTTP Basic 认证，
    /// 对应图形界面里的“Dashboard 用户名 / 密码”。
    /// </summary>
    internal sealed class FrpsApi
    {
        private static readonly string[] ProxyTypes =
            { "tcp", "udp", "http", "https", "tcpmux", "stcp", "xtcp", "sudp" };

        private readonly string _baseUrl;
        private readonly string _authHeader;
        private readonly int _timeoutMs;

        public FrpsApi(string host, int port, string user, string password, int timeoutMs = 6000)
        {
            if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
            _baseUrl = "http://" + host.Trim() + ":" + port;
            _timeoutMs = timeoutMs;

            if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(password))
            {
                string raw = (user ?? "") + ":" + (password ?? "");
                _authHeader = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            }
        }

        public string BaseUrl { get { return _baseUrl; } }

        /// <summary>探活：能读到版本号就说明服务端在跑、Dashboard 开着、账号密码也对。</summary>
        public string ProbeServerInfo()
        {
            var root = Json.AsObject(Json.Parse(Get("/api/serverinfo")));
            return Json.Str(root, "version", "");
        }

        public List<FrpsClientInfo> GetClients()
        {
            var list = new List<FrpsClientInfo>();
            var arr = Json.AsArray(Json.Parse(Get("/api/clients")));
            if (arr == null) return list;

            foreach (object item in arr)
            {
                list.Add(new FrpsClientInfo
                {
                    Key = Json.Str(item, "key", ""),
                    User = Json.Str(item, "user", ""),
                    ClientID = Json.Str(item, "clientID", ""),
                    RunID = Json.Str(item, "runID", ""),
                    Version = Json.Str(item, "version", ""),
                    Hostname = Json.Str(item, "hostname", ""),
                    ClientIP = Json.Str(item, "clientIP", ""),
                    Online = Json.Bool(item, "online", false)
                });
            }
            return list;
        }

        /// <summary>把 8 类代理一次性拉回来。某一类失败不影响其它类（比如老版本 frp 没有 sudp）。</summary>
        public List<FrpsProxyInfo> GetProxies()
        {
            var list = new List<FrpsProxyInfo>(16);

            foreach (string type in ProxyTypes)
            {
                string body;
                try { body = Get("/api/proxy/" + type); }
                catch { continue; }

                object root;
                try { root = Json.Parse(body); }
                catch { continue; }

                var arr = Json.AsArray(Json.Get(root, "proxies"));
                if (arr == null) continue;

                foreach (object item in arr)
                {
                    object conf = Json.Get(item, "conf");
                    var p = new FrpsProxyInfo
                    {
                        Name = Json.Str(item, "name", ""),
                        Type = type,
                        User = Json.Str(item, "user", ""),
                        ClientID = Json.Str(item, "clientID", ""),
                        Status = Json.Str(item, "status", ""),
                        RemotePort = Json.Int(conf, "remotePort", 0),
                        LocalPort = Json.Int(conf, "localPort", 0),
                        LocalIP = Json.Str(conf, "localIP", ""),
                        Subdomain = Json.Str(conf, "subdomain", "")
                    };
                    p.Domains.AddRange(Json.StrArray(conf, "customDomains"));
                    list.Add(p);
                }
            }
            return list;
        }

        // ------------------------------------------------------------------

        private string Get(string path)
        {
            var req = (HttpWebRequest)WebRequest.Create(_baseUrl + path);
            req.Method = "GET";
            req.Timeout = _timeoutMs;
            req.ReadWriteTimeout = _timeoutMs;
            // 关键：不要把 127.0.0.1 交给系统代理，否则装了代理软件的机器会连不上
            req.Proxy = null;
            req.KeepAlive = false;
            req.Accept = "application/json";
            req.UserAgent = "FrpWin";
            if (_authHeader != null) req.Headers["Authorization"] = _authHeader;

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>把网络异常翻译成用户能照着做的中文提示。</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return Loc.T("未知错误");

            var web = ex as WebException;
            if (web != null)
            {
                var resp = web.Response as HttpWebResponse;
                if (resp != null)
                {
                    int code = (int)resp.StatusCode;
                    if (code == 401 || code == 403)
                        return Loc.F("Dashboard 拒绝了访问（HTTP {0}）：用户名或密码不对。\r\n" +
                                     "请在“服务端”标签页里核对 Dashboard 用户名 / 密码。", code);
                    if (code == 404)
                        return Loc.T("Dashboard 里没有这个接口（HTTP 404）：这个 frps 版本可能太老，缺少 /api/clients。");
                    return Loc.F("Dashboard 返回 HTTP {0} {1}。", code, resp.StatusDescription);
                }

                switch (web.Status)
                {
                    case WebExceptionStatus.ConnectFailure:
                        return Loc.T("连接不上 Dashboard（连接被拒绝）。\r\n" +
                                     "请确认：① 服务端已启动；② “Dashboard 端口”填了大于 0 的端口并已保存生效；\r\n" +
                                     "③ Windows 防火墙没有拦住本机回环地址。");
                    case WebExceptionStatus.Timeout:
                        return Loc.T("连接 Dashboard 超时。请确认服务端正在运行，且 Dashboard 端口没有被别的程序占用。");
                    case WebExceptionStatus.NameResolutionFailure:
                        return Loc.F("Dashboard 地址解析失败：{0}", _SafeHost(ex));
                    default:
                        return Loc.F("访问 Dashboard 失败：{0}", web.Message);
                }
            }

            var io = ex as IOException;
            if (io != null) return Loc.F("读取 Dashboard 数据失败：{0}", io.Message);

            // Json 解析器抛出来的消息本身也可能带中文，这里按整串再翻一次
            if (ex is FormatException)
                return Loc.F("Dashboard 返回的内容不是合法 JSON：{0}", Loc.Whole(ex.Message));

            return ex.Message;
        }

        private static string _SafeHost(Exception ex)
        {
            try { return ex.Message; }
            catch { return ""; }
        }
    }
}
