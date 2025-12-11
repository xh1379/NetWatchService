using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Timers;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Management;

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
        private bool testMode;
        private bool autoStopOnNetworkOk;

        // New settings for hardware-level handling
        private bool enableAdapterReset;
        private int adapterResetRetries;
        private int adapterResetDelayMs;

        private enum NetworkHealthState
        {
            Healthy,
            MissingAdapter,
            AdapterDisabled,
            HardwareFault,
            MediaDisconnected,
            NoIpConfiguration,
            PendingRecovery,
            UnknownFailure
        }

        private sealed class NetworkHealthResult
        {
            private readonly NetworkHealthState _state;
            private readonly string _message;
            private readonly bool _shouldEscalate;

            private NetworkHealthResult(NetworkHealthState state, string message, bool shouldEscalate)
            {
                _state = state;
                _message = message;
                _shouldEscalate = shouldEscalate;
            }

            public NetworkHealthState State { get { return _state; } }
            public string Message { get { return _message; } }
            public bool ShouldEscalate { get { return _shouldEscalate; } }

            public static NetworkHealthResult Healthy(string message)
            {
                return new NetworkHealthResult(NetworkHealthState.Healthy, message, false);
            }

            public static NetworkHealthResult NonCritical(NetworkHealthState state, string message)
            {
                return new NetworkHealthResult(state, message, false);
            }

            public static NetworkHealthResult Critical(NetworkHealthState state, string message)
            {
                return new NetworkHealthResult(state, message, true);
            }

            public static NetworkHealthResult Pending(string message)
            {
                return new NetworkHealthResult(NetworkHealthState.PendingRecovery, message, false);
            }
        }

        private sealed class AdapterSnapshot
        {
            private readonly string _name;
            private readonly uint? _status;
            private readonly bool? _netEnabled;

            public AdapterSnapshot(ManagementObject source)
            {
                _name = Convert.ToString(source["Name"]) ?? "Unknown";
                _status = GetNullableUInt(source["NetConnectionStatus"]);
                _netEnabled = GetNullableBool(source["NetEnabled"]);
            }

            public string Name { get { return _name; } }
            public uint? Status { get { return _status; } }
            public bool? NetEnabled { get { return _netEnabled; } }

            public bool IsOperational
            {
                get { return Status == 2 || NetEnabled == true; }
            }

            public bool IndicatesHardwareFault
            {
                get { return Status == 4 || Status == 5 || Status == 6; }
            }

            public bool IndicatesMediaDisconnected
            {
                get { return Status == 7; }
            }

            private static uint? GetNullableUInt(object value)
            {
                if (value == null)
                    return null;
                try
                {
                    return Convert.ToUInt32(value);
                }
                catch
                {
                    return null;
                }
            }

            private static bool? GetNullableBool(object value)
            {
                if (value == null)
                    return null;
                try
                {
                    return Convert.ToBoolean(value);
                }
                catch
                {
                    return null;
                }
            }
        }

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
                maxCheckCount = ParseIntSetting(settings, "MaxCheckCount", 10);
                failureThreshold = ParseIntSetting(settings, "FailureThreshold", 3);
                testMode = ParseBoolSetting(settings, "TestMode", false);
                autoStopOnNetworkOk = ParseBoolSetting(settings, "AutoStopOnNetworkOk", true);

                // new settings
                enableAdapterReset = ParseBoolSetting(settings, "EnableAdapterReset", true);
                adapterResetRetries = ParseIntSetting(settings, "AdapterResetRetries", 3);
                adapterResetDelayMs = ParseIntSetting(settings, "AdapterResetDelayMs", 5000);
            }
            catch
            {
                // fallback hard-coded defaults if config read fails
                logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetWatchService", "log.txt");
                intervalMs = 60000;
                maxCheckCount = 10;
                failureThreshold = 3;
                testMode = false;
                autoStopOnNetworkOk = true;

                enableAdapterReset = true;
                adapterResetRetries = 3;
                adapterResetDelayMs = 5000;
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
                NetworkHealthResult health = CheckNetwork();

                if (health.State == NetworkHealthState.Healthy)
                {
                    consecutiveFailures = 0;
                    WriteLog("网络硬件状态正常");

                    if (autoStopOnNetworkOk)
                    {
                        WriteLog("网络正常 - 服务即将停止");
                        if (timer != null)
                            timer.Stop();
                        this.Stop();
                        return;
                    }

                    return;
                }

                if (!health.ShouldEscalate)
                {
                    consecutiveFailures = 0;
                    if (health.State == NetworkHealthState.PendingRecovery)
                    {
                        WriteLog("网卡正在尝试恢复，等待下一轮检查");
                    }
                    else
                    {
                        WriteLog(string.Format("检测到可恢复或外部原因导致的问题: {0}", health.Message));
                    }
                    return;
                }

                consecutiveFailures++;
                WriteLog(string.Format("检测到硬件级异常 ({0})，连续失败 {1}/{2}", health.Message, consecutiveFailures, failureThreshold));

                if (consecutiveFailures >= failureThreshold || checkCount >= maxCheckCount)
                {
                    WriteLog("硬件状态持续异常，准备重启计算机...");
                    RestartComputer();
                    return;
                }
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

        private NetworkHealthResult CheckNetwork()
        {
            try
            {
                var physicalAdapters = GetPhysicalAdapters();
                if (physicalAdapters.Count == 0)
                {
                    WriteLog("未检测到物理网卡（没有被枚举）");

                    if (enableAdapterReset)
                    {
                        WriteLog("尝试重新扫描或重置网卡设备...");
                        bool fixedUp = TryResetAdapters(adapterResetRetries, adapterResetDelayMs);
                        if (fixedUp)
                        {
                            WriteLog("重置后检测到网卡，等待下一次检查确认");
                            return NetworkHealthResult.Pending("网卡重置后等待状态稳定");
                        }
                    }

                    return NetworkHealthResult.Critical(NetworkHealthState.MissingAdapter, "未检测到任何物理网卡");
                }

                var snapshots = physicalAdapters.Select(a => new AdapterSnapshot(a)).ToList();

                if (snapshots.Any(s => s.IndicatesHardwareFault))
                {
                    WriteLog("至少有一个网卡报告硬件故障/驱动异常");
                    return NetworkHealthResult.Critical(NetworkHealthState.HardwareFault, "网卡硬件或驱动故障");
                }

                if (snapshots.Any(s => s.IsOperational))
                {
                    if (HasValidIpConfiguration())
                    {
                        WriteLog("网卡已启用并获取IP设置");
                        return NetworkHealthResult.Healthy("网卡状态正常");
                    }

                    WriteLog("网卡已启用但未获得IP（可能是DHCP或网络侧问题）");
                    return NetworkHealthResult.NonCritical(NetworkHealthState.NoIpConfiguration, "缺少IP地址/网关");
                }

                if (snapshots.All(s => s.IndicatesMediaDisconnected))
                {
                    WriteLog("所有网卡均显示介质断开，可能是网线或上游设备断电");

                    // 在断电恢复场景下，网卡可能在短时间内仍报告介质断开。尝试进行一次重置并等待下一轮检查，
                    // 而不是立即将其计为严重故障或触发重启。
                    if (enableAdapterReset)
                    {
                        WriteLog("尝试重置适配器以恢复连接...");
                        bool reset = TryResetAdapters(adapterResetRetries, adapterResetDelayMs);
                        if (reset)
                        {
                            WriteLog("重置成功，等待下一轮检查确认");
                            return NetworkHealthResult.Pending("介质断开: 重置后等待状态稳定");
                        }
                        else
                        {
                            WriteLog("重置未能恢复连接，短暂等待下一次检查");
                            return NetworkHealthResult.NonCritical(NetworkHealthState.MediaDisconnected, "介质断开，重置未恢复");
                        }
                    }

                    return NetworkHealthResult.NonCritical(NetworkHealthState.MediaDisconnected, "介质断开");
                }

                WriteLog("发现物理网卡但没有处于 Up 状态，尝试恢复");

                if (enableAdapterReset)
                {
                    bool resetOk = TryResetAdapters(adapterResetRetries, adapterResetDelayMs);
                    if (resetOk)
                    {
                        WriteLog("网卡重置后已就绪");
                        return NetworkHealthResult.Pending("网卡刚完成重置");
                    }
                }

                return NetworkHealthResult.Critical(NetworkHealthState.AdapterDisabled, "网卡无法被启用");
            }
            catch (Exception ex)
            {
                WriteLog("网络检查异常: " + ex.Message);
                return NetworkHealthResult.Critical(NetworkHealthState.UnknownFailure, "网络检查异常");
            }
        }

        private List<ManagementObject> GetPhysicalAdapters()
        {
            var list = new List<ManagementObject>();
            try
            {
                string q = "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE";
                using (var search = new ManagementObjectSearcher(q))
                {
                    foreach (ManagementObject mo in search.Get())
                    {
                        list.Add(mo);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("GetPhysicalAdapters 异常: " + ex.Message);
            }
            return list;
        }

        private bool IsAdapterOperational(ManagementObject mo)
        {
            try
            {
                var status = mo["NetConnectionStatus"];
                var netEnabled = mo["NetEnabled"];
                if (status != null && (uint)Convert.ToUInt32(status) == 2)
                    return true;
                if (netEnabled != null && (bool)netEnabled == true)
                    return true; // enabled but may not be connected
            }
            catch { }
            return false;
        }

        private bool TryResetAdapters(int retries, int delayMs)
        {
            try
            {
                for (int attempt = 0; attempt < retries; attempt++)
                {
                    WriteLog(string.Format("重置尝试 {0}/{1}", attempt + 1, retries));

                    var adapters = GetPhysicalAdapters();
                    foreach (var mo in adapters)
                    {
                        try
                        {
                            var netEnabled = mo["NetEnabled"];
                            if (netEnabled == null || (bool)netEnabled == false)
                            {
                                WriteLog(string.Format("尝试启用适配器: {0}", mo["Name"]));
                                try { mo.InvokeMethod("Enable", null); } catch { }
                            }
                            else
                            {
                                WriteLog(string.Format("尝试重启适配器: {0}", mo["Name"]));
                                try { mo.InvokeMethod("Disable", null); } catch { }
                                System.Threading.Thread.Sleep(500);
                                try { mo.InvokeMethod("Enable", null); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLog("Reset adapter exception: " + ex.Message);
                        }
                    }

                    System.Threading.Thread.Sleep(delayMs);

                    var adaptersUp = GetPhysicalAdapters().Where(a => IsAdapterOperational(a)).ToList();
                    if (adaptersUp.Count > 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                WriteLog("TryResetAdapters 异常: " + ex.Message);
            }
            return false;
        }

        private bool HasValidIpConfiguration()
        {
            try
            {
                string q = "SELECT IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE";
                using (var search = new ManagementObjectSearcher(q))
                {
                    foreach (ManagementObject mo in search.Get())
                    {
                        var addresses = mo["IPAddress"] as string[];
                        if (addresses != null && addresses.Any(ip => !string.IsNullOrWhiteSpace(ip)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("HasValidIpConfiguration 异常: " + ex.Message);
            }
            return false;
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
