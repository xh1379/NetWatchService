# NetWatchService

A Windows service that monitors network adapter hardware status and automatically restarts the system when network failures are detected.

## Features

- **Hardware-level network diagnostics** using WMI queries
- **Intelligent failure classification**: Distinguishes hardware faults, media disconnects, and recoverable issues
- **Automatic adapter reset**: Attempts to recover network adapters before escalating
- **Configurable thresholds**: Customizable retry counts, intervals, and failure limits
- **Safe power-recovery handling**: Gracefully handles post-reboot network initialization
- **Detailed logging**: Tracks every check, reset attempt, and decision

---

## Quick Start

### One-Click Installation (Recommended)

1. **Right-click `install.bat` → Run as Administrator**
2. The script will:
   - ? Check environment and permissions
   - ? Stop any existing service
   - ? Build the project (Release mode)
   - ? Copy files to `C:\NetWatchService\`
   - ? Install and start the service

### Manual Installation

```powershell
# 1. Build the project in Visual Studio (Release mode)

# 2. Open PowerShell as Administrator

# 3. Create installation directory
New-Item -Path "C:\NetWatchService" -ItemType Directory -Force

# 4. Copy binaries
Copy-Item "bin\Release\NetWatchService.exe" "C:\NetWatchService\"

# 5. Install service
sc.exe create NetWatchService binPath= "C:\NetWatchService\NetWatchService.exe" start= auto DisplayName= "Network Watch Service"
sc.exe description NetWatchService "Monitors network hardware and restarts system on persistent failures"

# 6. Configure failure recovery
sc.exe failure NetWatchService reset= 86400 actions= restart/60000/restart/60000/restart/60000

# 7. Start service
sc.exe start NetWatchService
```

---

## Configuration

### File Location
- **Primary**: `%ProgramData%\NetWatchService\settings.ini`
- **Fallback**: `C:\NetWatchService\settings.ini` (installation directory)

### Settings Reference

| Setting | Default | Description |
|---------|---------|-------------|
| `LogPath` | `%ProgramData%\NetWatchService\log.txt` | Log file location (supports environment variables) |
| `IntervalMs` | `60000` | Check interval in milliseconds (1 minute) |
| `MaxCheckCount` | `10` | Maximum checks before forced restart |
| `FailureThreshold` | `3` | Consecutive critical failures required to trigger restart |
| `EnableAdapterReset` | `true` | Allow automatic adapter disable/enable attempts |
| `AdapterResetRetries` | `3` | Number of reset attempts per check cycle |
| `AdapterResetDelayMs` | `5000` | Wait time after each reset attempt (5 seconds) |
| `TestMode` | `false` | Simulate restart without executing `shutdown` command |
| `AutoStopOnNetworkOk` | `true` | Stop service when network becomes healthy |

### Example Configuration

```ini
# NetWatchService settings
LogPath=%ProgramData%\NetWatchService\log.txt
IntervalMs=30000
MaxCheckCount=10
FailureThreshold=3
EnableAdapterReset=true
AdapterResetRetries=3
AdapterResetDelayMs=5000
TestMode=false
AutoStopOnNetworkOk=true
```

---

## Management

### Using Manage Script (Recommended)

**Right-click `manage.bat` → Run as Administrator**

Available options:
- ?? Start/Stop/Restart service
- ?? View service status
- ?? View recent logs (last 20 lines)
- ?? Open full log in Notepad
- ?? Clear logs
- ?? Edit configuration

### Using Windows Commands

```powershell
# View status
sc query NetWatchService

# Start service
sc start NetWatchService

# Stop service
sc stop NetWatchService

# View logs
type %ProgramData%\NetWatchService\log.txt

# View recent logs
powershell "Get-Content '%ProgramData%\NetWatchService\log.txt' -Tail 20"
```

### Using Services.msc

1. Press `Win + R`, type `services.msc`
2. Find "Network Watch Service" or "NetWatchService"
3. Right-click → Start/Stop/Restart

---

## How It Works

### Health Check Flow

1. **Query Physical Adapters**: Uses WMI to enumerate all physical network adapters
2. **Classify Status**:
   - ? **Healthy**: Adapter operational with valid IP configuration
   - ?? **Non-Critical**: Missing IP (DHCP issue), media disconnected (cable/switch)
   - ?? **Critical**: Hardware fault, driver error, adapter disabled and unrecoverable
   - ?? **Pending**: Reset in progress, awaiting next check
3. **Attempt Recovery**: For recoverable issues, tries to reset adapter (disable/enable)
4. **Escalate**: After consecutive critical failures exceed threshold, triggers system restart

### Decision Logic

```
Network Check
│
├─ No Adapters Found → Try Reset → Still None → CRITICAL
├─ Hardware Fault (Status 4/5/6) → CRITICAL
├─ At Least One Operational
│  ├─ Has IP → HEALTHY
│  └─ No IP → NON-CRITICAL (DHCP/network issue)
├─ All Media Disconnected (Status 7)
│  ├─ Try Reset → Success → PENDING
│  └─ Fail → CRITICAL
└─ Adapters Present but Not Up
   ├─ Try Reset → Success → PENDING
   └─ Fail → CRITICAL
```

### Restart Trigger Conditions

System restart occurs when **ANY** of:
- Consecutive critical failures ≥ `FailureThreshold` (default: 3)
- Total checks ≥ `MaxCheckCount` (default: 10)
- Exception during check exceeds threshold

---

## Log Reference

### Log Location
```
%ProgramData%\NetWatchService\log.txt
```
(Typically: `C:\ProgramData\NetWatchService\log.txt`)

### Sample Log

```
2025-12-11 14:18:32 - Service started - 开始检查网络状态
2025-12-11 14:18:32 - 第 1 次检查网络...
2025-12-11 14:18:32 - 所有网卡均显示介质断开，可能是网线或上游设备断电
2025-12-11 14:18:32 - 尝试重置适配器以恢复连接...
2025-12-11 14:18:32 - 重置尝试 1/3
2025-12-11 14:18:33 - 尝试启用适配器: Intel(R) Ethernet Connection (7) I219-V
2025-12-11 14:18:38 - 重置尝试 2/3
2025-12-11 14:18:43 - 重置尝试 3/3
2025-12-11 14:18:48 - 重置未能恢复连接，多次失败后将升级处理
2025-12-11 14:18:48 - 检测到硬件级异常 (介质断开，多次重置失败)，连续失败 1/3
2025-12-11 14:19:18 - 第 2 次检查网络...
2025-12-11 14:19:18 - 检测到硬件级异常 (介质断开，多次重置失败)，连续失败 2/3
2025-12-11 14:19:48 - 第 3 次检查网络...
2025-12-11 14:19:48 - 检测到硬件级异常 (介质断开，多次重置失败)，连续失败 3/3
2025-12-11 14:19:48 - 硬件状态持续异常，准备重启计算机...
2025-12-11 14:19:48 - 正在执行重启命令...
```

---

## Uninstallation

### One-Click Uninstall (Recommended)

**Right-click `uninstall.bat` → Run as Administrator**

Prompts to optionally delete installation files and logs.

### Manual Uninstall

```powershell
# Stop service
sc stop NetWatchService

# Delete service
sc delete NetWatchService

# Remove files (optional)
Remove-Item -Path "C:\NetWatchService" -Recurse -Force
Remove-Item -Path "%ProgramData%\NetWatchService" -Recurse -Force
```

---

## Troubleshooting

### Service Won't Start

1. Verify Administrator permissions
2. Check Event Viewer: `Win + R` → `eventvwr.msc` → Windows Logs → Application
3. Review log file: `%ProgramData%\NetWatchService\log.txt`

### Service Stops Immediately

Expected behavior when network is healthy and `AutoStopOnNetworkOk=true`. Check logs to confirm.

### System Doesn't Restart

1. Ensure consecutive failures reach threshold (default: 3)
2. Verify service has permissions to execute `shutdown` command
3. Check for error messages in logs
4. Test with `TestMode=true` to verify detection logic

### False Positives

- Increase `FailureThreshold` to require more consecutive failures
- Increase `AdapterResetRetries` to allow more recovery attempts
- Review logs to identify root cause (hardware vs. transient)

---

## Important Notes

1. **Administrator Permissions Required**: Installation, configuration, and runtime all require admin rights
2. **Auto-Start**: Service is configured to start automatically on system boot
3. **Forced Restart**: System restart is forced (`shutdown /r /f`), unsaved work will be lost
4. **Log Growth**: Log files grow indefinitely; consider periodic manual cleanup
5. **Firewall/Security**: No network traffic is generated (uses local WMI queries only)

---

## FAQ

**Q: When does the service start checking?**  
A: Immediately upon service start, then at regular intervals.

**Q: Will the service run forever?**  
A: No, if network becomes healthy and `AutoStopOnNetworkOk=true`, service stops automatically.

**Q: How do I test without restarting?**  
A: Set `TestMode=true` in configuration and monitor logs.

**Q: Can I prevent restart and only reset adapters?**  
A: Not currently supported. Consider modifying the source code to remove `RestartComputer()` call.

**Q: What happens during power outages?**  
A: On reboot, adapters may temporarily report "media disconnected." Service will attempt reset and wait for confirmation before escalating.

---

## Technical Details

### WMI Queries Used

- `Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE`
- `Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE`

### Adapter Status Codes

| Code | Meaning | Service Action |
|------|---------|----------------|
| 2 | Connected | Healthy |
| 4 | Disconnected | Hardware fault (critical) |
| 5 | Disconnecting | Hardware fault (critical) |
| 6 | Disconnecting | Hardware fault (critical) |
| 7 | Media Disconnected | Try reset, then critical if unrecoverable |

### Requirements

- Windows 7 / Server 2008 R2 or later
- .NET Framework 4.8
- Administrator privileges

---

## Project Structure

| File | Description |
|------|-------------|
| `NetworkMonitorService.cs` | Main service implementation |
| `Program.cs` | Service entry point |
| `install.bat` | One-click installation script |
| `uninstall.bat` | One-click uninstall script |
| `manage.bat` | Service management utility |
| `settings.ini` | Configuration template |

---

## License

This project is for learning and internal use.

---

## Version History

- **v1.3** (2025-12) - Hardware-focused diagnostics, adapter reset flow, fixed media-disconnect escalation bug
- **v1.2** - Added one-click scripts and management utility
- **v1.1** - Auto-stop feature, multiple DNS targets
- **v1.0** - Initial release (network monitoring + automatic restart)

---

**Maintainer**: NetWatchService  
**Year**: 2024-2025
