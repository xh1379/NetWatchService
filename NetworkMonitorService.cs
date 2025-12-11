using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Timers;
using System.IO;
using System.Linq;

namespace NetWatchService
{
    public class NetworkMonitorService : ServiceBase
    {
        private Timer timer;
        private string logPath;
        private int checkCount = 0;
        private int maxCheckCount;
        private int consecutiveFailures = 0;
        private int failureThreshold;
        private int intervalMs;
        private int pingTimeoutMs;
        private string[] pingTargets;
        private bool testMode;
        private bool autoStopOnNetworkOk;

        public NetworkMonitorService()
        {
            this.ServiceName = "NetWatchService";

            // Load configuration with defaults
            try
            {
                // Defaults
                string defaultLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetWatchService");
                string defaultLogPath = Path.Combine(defaultLogDir, "log.txt");

                // config file locations (check common appdata then executable folder)
                string cfgDir = defaultLogDir;
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string cfgFile1 = Path.Combine(cfgDir, "settings.ini");
                string cfgFile2 = Path.Combine(exeDir, "settings.ini");

                // read settings from file if exists
                var settings = ReadSettings(cfgFile1);
                if (settings.Count == 0)
                    settings = ReadSettings(cfgFile2);

                string cfgLogPath = GetSetting(settings, "LogPath", defaultLogPath);
                cfgLogPath = Environment.ExpandEnvironmentVariables(cfgLogPath);
                logPath = cfgLogPath;

                intervalMs = ParseIntSetting(settings, "IntervalMs", 60000);
                pingTimeoutMs = ParseIntSetting(settings, "PingTimeoutMs", 3000);
                maxCheckCount = ParseIntSetting(settings, "MaxCheckCount", 10);
                failureThreshold = ParseIntSetting(settings, "FailureThreshold", 3);
                testMode = ParseBoolSetting(settings, "TestMode", false);
                autoStopOnNetworkOk = ParseBoolSetting(settings, "AutoStopOnNetworkOk", true);

                string targets = GetSetting(settings, "PingTargets", "8.8.8.8,114.114.114.114");
                pingTargets = targets.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(t => t.Trim()).ToArray();
            }
            catch
            {
                // fallback hard-coded defaults if config read fails
                logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetWatchService", "log.txt");
                intervalMs = 60000;
                pingTimeoutMs = 3000;
                maxCheckCount = 10;
                failureThreshold = 3;
                pingTargets = new[] { "8.8.8.8", "114.114.114.114" };
                testMode = false;
                autoStopOnNetworkOk = true;
            }
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                string dir = Path.GetDirectoryName(logPath);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
                }
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }

            WriteLog("Service started - 开始检查网络状态");

            timer = new Timer(intervalMs); // configurable interval
            timer.Elapsed += Timer_Elapsed;
            timer.AutoReset = true;
            timer.Start();

            // 启动后立即执行第一次检查
            Timer_Elapsed(null, null);
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            checkCount++;
            WriteLog(string.Format("第 {0} 次检查网络...", checkCount));

            try
            {
                bool networkOk = CheckNetwork();

                if (networkOk)
                {
                    consecutiveFailures = 0;
                    WriteLog("网络正常");

                    if (autoStopOnNetworkOk)
                    {
                        WriteLog("网络正常 - 服务即将停止");
                        if (timer != null)
                            timer.Stop();
                        this.Stop();
                        return;
                    }
                }
                else
                {
                    consecutiveFailures++;
                    WriteLog(string.Format("网络异常 (连续失败: {0}/{1})", consecutiveFailures, failureThreshold));

                    if (consecutiveFailures >= failureThreshold)
                    {
                        WriteLog("网络持续异常，准备重启计算机...");
                        RestartComputer();
                        return;
                    }
                    else if (checkCount >= maxCheckCount)
                    {
                        WriteLog(string.Format("已检查 {0} 次，网络仍不稳定，准备重启计算机...", maxCheckCount));
                        RestartComputer();
                        return;
                    }
                }

                // if not decided to stop/restart, continue timer
            }
            catch (Exception ex)
            {
                WriteLog("检查过程出错: " + ex.Message);
                consecutiveFailures++;

                if (consecutiveFailures >= failureThreshold || checkCount >= maxCheckCount)
                {
                    WriteLog("多次检查失败，准备重启计算机...");
                    RestartComputer();
                }
            }
        }

        private bool CheckNetwork()
        {
            try
            {
                // 方法1: Ping 列表中的目标
                using (Ping ping = new Ping())
                {
                    foreach (var target in pingTargets)
                    {
                        try
                        {
                            PingReply reply = ping.Send(target, pingTimeoutMs);
                            if (reply != null && reply.Status == IPStatus.Success)
                            {
                                WriteLog(string.Format("Ping {0} 成功 - 延迟: {1}ms", target, reply.RoundtripTime));
                                return true;
                            }
                            else
                            {
                                WriteLog(string.Format("Ping {0} 失败: {1}", target, reply == null ? "no reply" : reply.Status.ToString()));
                            }
                        }
                        catch (Exception exPing)
                        {
                            WriteLog(string.Format("Ping {0} 抛出异常: {1}", target, exPing.Message));
                        }
                    }
                }

                // 方法2: 检查网卡状态
                bool hasActiveAdapter = false;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && 
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        hasActiveAdapter = true;
                        WriteLog(string.Format("发现活动网卡: {0} ({1})", ni.Name, ni.NetworkInterfaceType));
                    }
                }

                if (!hasActiveAdapter)
                {
                    WriteLog("未发现活动的网卡");
                }

                return false;
            }
            catch (Exception ex)
            {
                WriteLog("网络检查异常: " + ex.Message);
                return false;
            }
        }

        private void RestartComputer()
        {
            try
            {
                if (timer != null)
                    timer.Stop();
                WriteLog("正在执行重启命令...");

                if (testMode)
                {
                    WriteLog("TEST MODE: 模拟重启（未执行 shutdown）");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /f /t 10 /c \"NetWatchService: 网络异常，系统将重启\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                WriteLog("重启失败: " + ex.Message);
            }
        }

        protected override void OnStop()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }
            WriteLog("Service stopped");
        }

        private void WriteLog(string message)
        {
            try
            {
                string logMessage = string.Format("{0:yyyy-MM-dd HH:mm:ss} - {1}", DateTime.Now, message);
                File.AppendAllText(logPath, logMessage + Environment.NewLine);
            }
            catch { }
        }

        private System.Collections.Generic.Dictionary<string, string> ReadSettings(string path)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path))
                    return dict;

                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (string.IsNullOrEmpty(t) || t.StartsWith("#") || t.StartsWith(";"))
                        continue;
                    int idx = t.IndexOf('=');
                    if (idx <= 0)
                        continue;
                    string k = t.Substring(0, idx).Trim();
                    string v = t.Substring(idx + 1).Trim();
                    if (!dict.ContainsKey(k))
                        dict[k] = v;
                }
            }
            catch { }
            return dict;
        }

        private string GetSetting(System.Collections.Generic.Dictionary<string, string> settings, string key, string defaultValue)
        {
            if (settings != null)
            {
                string v;
                if (settings.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
                    return v;
            }
            return defaultValue;
        }

        private int ParseIntSetting(System.Collections.Generic.Dictionary<string, string> settings, string key, int defaultValue)
        {
            try
            {
                string v = GetSetting(settings, key, null);
                if (!string.IsNullOrEmpty(v))
                {
                    int r;
                    if (int.TryParse(v, out r))
                        return r;
                }
            }
            catch { }
            return defaultValue;
        }

        private bool ParseBoolSetting(System.Collections.Generic.Dictionary<string, string> settings, string key, bool defaultValue)
        {
            try
            {
                string v = GetSetting(settings, key, null);
                if (!string.IsNullOrEmpty(v))
                {
                    bool r;
                    if (bool.TryParse(v, out r))
                        return r;
                }
            }
            catch { }
            return defaultValue;
        }
    }
}
