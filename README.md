# NetWatchService

NetWatchService is a Windows Service that monitors physical network adapters and attempts automated recovery before escalating to a system restart when persistent hardware-level network failures are detected.

## Key Features

- Hardware-level diagnostics via WMI
- Classifies issues: Healthy, Non-critical (e.g., no IP), Media disconnected, Hardware fault
- Automatic adapter reset (disable/enable) attempts
- Configurable thresholds and intervals through `settings.ini`
- Test mode to avoid executing real shutdowns
- Detailed plain-text logging to a configurable log file

---

## Quick Start

Recommended: run `install.bat` as Administrator. The script will build, copy files to `C:\NetWatchService`, install the service, and start it.

Manual install steps (PowerShell as Administrator):

```powershell
New-Item -Path "C:\NetWatchService" -ItemType Directory -Force
Copy-Item "bin\Release\NetWatchService.exe" "C:\NetWatchService\"
sc.exe create NetWatchService binPath= "C:\NetWatchService\NetWatchService.exe" start= auto DisplayName= "Network Watch Service"
sc.exe description NetWatchService "Monitors network hardware and restarts system on persistent failures"
sc.exe failure NetWatchService reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start NetWatchService
```

---

## Configuration

Primary configuration file: `%ProgramData%\NetWatchService\settings.ini` (fallback: `C:\NetWatchService\settings.ini`).

Relevant settings (defaults shown):

| Setting | Default | Description |
|---------|---------|-------------|
| `LogPath` | `%ProgramData%\NetWatchService\log.txt` | Path for the service log (supports environment variables) |
| `IntervalMs` | `30000` | Check interval in milliseconds (30 seconds) |
| `MaxCheckCount` | `10` | Maximum total checks before forced restart |
| `FailureThreshold` | `3` | Consecutive critical failures required to trigger restart |
| `EnableAdapterReset` | `true` | Allow automatic adapter disable/enable attempts |
| `AdapterResetRetries` | `3` | Reset attempts per check cycle |
| `AdapterResetDelayMs` | `5000` | Wait time after each reset attempt (ms) |
| `TestMode` | `false` | If true, do not execute `shutdown` (safe testing) |
| `AutoStopOnNetworkOk` | `true` | Stop the service automatically when network becomes healthy |

Example `settings.ini`:

```ini
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

## How It Works

1. Enumerates physical adapters via WMI (`Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE`).
2. For each adapter, reads `NetConnectionStatus` and `NetEnabled` to form a snapshot.
3. Classification:
   - Healthy: at least one adapter operational with a valid IP.
   - Non-critical: adapter enabled but no IP (DHCP/network-side issue).
   - Media disconnected: link-state shows media disconnected (often cable/switch/initialization issue).
   - Hardware fault: adapter reports hardware/driver errors.
4. Recovery: for recoverable states the service attempts limited `Disable`/`Enable` cycles.
5. Escalation: if a state is Critical and persists for `FailureThreshold` consecutive checks (or total checks reach `MaxCheckCount`), the service issues a system restart (unless `TestMode=true`).

---

## Logs

Default log file: `%ProgramData%\NetWatchService\log.txt`. The log contains timestamps, check counts, decisions and reset attempt details.

Sample log excerpt:

```
2025-12-11 14:21:02 - Service started - starting network checks
2025-12-11 14:21:02 - Check #1
2025-12-11 14:21:02 - All adapters report media disconnected
2025-12-11 14:21:02 - Attempting adapter reset (1/3)
2025-12-11 14:21:07 - Adapter reset failed
2025-12-11 14:21:27 - Check #2
2025-12-11 14:21:27 - Critical: media disconnected (reset failed)
2025-12-11 14:21:27 - Consecutive critical failures 1/3
```

Notes:
- Logs grow indefinitely; consider adding rotation/cleanup in production.

---

## Management

Use `manage.bat` (Run as Administrator) to start, stop, restart, view status, view logs, or edit configuration.

CLI shortcuts:
- `sc query NetWatchService`
- `sc start NetWatchService`
- `sc stop NetWatchService`

---

## Troubleshooting

- Service does not start: verify Administrator rights and check the log and Windows Event Viewer application logs.
- No restart occurs: ensure `TestMode=false`, verify `FailureThreshold` and log entries showing consecutive critical failures.
- Frequent false positives: increase `FailureThreshold` or `AdapterResetRetries` and inspect raw adapter WMI values in the log.

---

## Development Notes

- Target framework: .NET Framework 4.8
- Keep the service running under an account with adequate privileges to manage network adapters and execute `shutdown`.

---

## License

Internal use / learning project.
