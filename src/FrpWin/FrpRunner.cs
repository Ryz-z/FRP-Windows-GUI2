using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FrpWin
{
    /// <summary>启动 / 停止 frps.exe、frpc.exe 并实时抓取它们的控制台输出。</summary>
    internal sealed class FrpRunner : IDisposable
    {
        private readonly object _lock = new object();
        private Process _proc;
        private StreamWriter _fileLog;

        /// <summary>每收到一行输出触发一次（已去掉换行符）。</summary>
        public event Action<string> Line;

        public string Role { get; private set; }

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    try { return _proc != null && !_proc.HasExited; }
                    catch { return false; }
                }
            }
        }

        public int ProcessId
        {
            get
            {
                lock (_lock)
                {
                    try { return _proc != null && !_proc.HasExited ? _proc.Id : 0; }
                    catch { return 0; }
                }
            }
        }

        /// <summary>启动 frp 进程。logFile 不为空时同时把输出追加写入该文件。</summary>
        public void Start(string role, string exePath, string configPath, string workingDir, string logFile)
        {
            Start(role, exePath, configPath, workingDir, logFile, null);
        }

        /// <summary>
        /// tag 是这个进程在日志里的代号（多连接时用连接名），只影响日志文字，不影响命令行。
        /// </summary>
        public void Start(string role, string exePath, string configPath, string workingDir, string logFile, string tag)
        {
            Stop();
            Role = role;
            string who = string.IsNullOrEmpty(tag) ? role : tag;

            if (!File.Exists(exePath))
                throw new FileNotFoundException(Loc.F("找不到可执行文件：{0}", exePath), exePath);
            if (!File.Exists(configPath))
                throw new FileNotFoundException(Loc.F("找不到配置文件：{0}", configPath), configPath);

            if (!string.IsNullOrEmpty(logFile))
            {
                try
                {
                    RotateLog(logFile);
                    _fileLog = new StreamWriter(new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                    _fileLog.AutoFlush = true;
                }
                catch { _fileLog = null; }
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-c \"" + configPath + "\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => Emit(e.Data);
            p.ErrorDataReceived += (s, e) => Emit(e.Data);
            p.Exited += (s, e) =>
            {
                int code = -1;
                try { code = p.ExitCode; } catch { }
                Emit(Loc.F("[{0}] 进程已退出，退出码 {1}。", who, code));
            };

            lock (_lock) { _proc = p; }

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch
            {
                lock (_lock) { _proc = null; }
                CloseFileLog();
                throw;
            }
        }

        /// <summary>等待子进程退出；milliseconds 为 -1 表示一直等待。</summary>
        public bool WaitForExit(int milliseconds)
        {
            Process p;
            lock (_lock) { p = _proc; }
            if (p == null) return true;
            try { return p.WaitForExit(milliseconds); }
            catch { return true; }
        }

        private void Emit(string line)
        {
            if (line == null) return;
            try
            {
                var w = _fileLog;
                if (w != null)
                {
                    // frp 自己的日志已经带时间戳了，避免写出两遍
                    string stamped = LooksTimestamped(line)
                        ? line
                        : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + line;

                    lock (_lock)
                    {
                        if (_fileLog != null) _fileLog.WriteLine(stamped);
                    }
                }
            }
            catch { }

            var handler = Line;
            if (handler != null)
            {
                try { handler(line); } catch { }
            }
        }

        /// <summary>判断一行是否已经以 "yyyy-MM-dd HH:mm:ss" 开头。</summary>
        private static bool LooksTimestamped(string s)
        {
            if (s.Length < 19) return false;
            return s[4] == '-' && s[7] == '-' && s[10] == ' ' && s[13] == ':' && s[16] == ':'
                   && char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[2]) && char.IsDigit(s[3]);
        }

        /// <summary>停止 frp 进程（等待 3 秒，超时后强制结束）。</summary>
        public void Stop()
        {
            Process p;
            lock (_lock) { p = _proc; _proc = null; }

            if (p != null)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(3000); } catch { }
                    }
                }
                catch { }
                try { p.Dispose(); } catch { }
            }

            CloseFileLog();
        }

        private void CloseFileLog()
        {
            lock (_lock)
            {
                try { if (_fileLog != null) { _fileLog.Flush(); _fileLog.Dispose(); } } catch { }
                _fileLog = null;
            }
        }

        private static void RotateLog(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 8L * 1024 * 1024)
                {
                    string bak = path + ".1";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
