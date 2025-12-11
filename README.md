# NetWatchService 部署说明

## ?? 项目简介

**NetWatchService** 是一个 Windows 系统服务，用于解决断电恢复后网卡无法正常工作的问题。

### 工作原理
1. 系统启动后自动运行（无需登录）
2. 每 60 秒检查一次网络状态
3. **网络正常** → 自动停止服务
4. **网络异常** → 连续失败 3 次后重启计算机
5. 最多检查 10 次（10 分钟）

---

## ?? 一键部署

### 方法一：使用自动部署脚本（推荐）

1. **右键点击 `install.bat`，选择「以管理员身份运行」**
2. 脚本会自动完成：
   - ? 检查环境和权限
   - ? 停止旧服务（如果存在）
   - ? 编译项目（Release 模式）
   - ? 复制文件到 `C:\NetWatchService\`
   - ? 安装并启动服务
3. 部署完成！

### 方法二：手动部署

```powershell
# 1. 使用 Visual Studio 编译项目（Release 模式）

# 2. 以管理员身份打开 PowerShell

# 3. 创建安装目录
New-Item -Path "C:\NetWatchService" -ItemType Directory -Force

# 4. 复制编译后的文件
Copy-Item "bin\Release\NetWatchService.exe" "C:\NetWatchService\"

# 5. 安装服务
sc.exe create NetWatchService binPath= "C:\NetWatchService\NetWatchService.exe" start= auto DisplayName= "网络监控服务"
sc.exe description NetWatchService "断电恢复后自动检查网络状态，异常时重启计算机"

# 6. 设置失败恢复策略
sc.exe failure NetWatchService reset= 86400 actions= restart/60000/restart/60000/restart/60000

# 7. 启动服务
sc.exe start NetWatchService
```

---

## ?? 文件说明

| 文件 | 说明 |
|------|------|
| `install.bat` | 一键安装部署脚本 |
| `uninstall.bat` | 一键卸载脚本 |
| `manage.bat` | 服务管理工具（启动/停止/查看日志等） |
| `NetworkMonitorService.cs` | 核心服务代码 |
| `Program.cs` | 服务入口 |

---

## ??? 服务管理

### 使用管理脚本（推荐）

**右键点击 `manage.bat`，选择「以管理员身份运行」**

提供以下功能：
- ?? 启动/停止/重启服务
- ?? 查看服务状态
- ?? 查看日志（最后 20 行或完整日志）
- ??? 清空日志
- ?? 测试网络连接

### 使用 Windows 命令

```powershell
# 查看服务状态
sc query NetWatchService

# 启动服务
sc start NetWatchService

# 停止服务
sc stop NetWatchService

# 查看日志
type C:\NetWatchService\log.txt

# 查看最后 20 行日志
powershell "Get-Content C:\NetWatchService\log.txt -Tail 20"
```

### 使用 Windows 服务管理器

1. 按 `Win + R`，输入 `services.msc`
2. 找到「网络监控服务」或「NetWatchService」
3. 右键 → 启动/停止/属性

---

## ?? 日志查看

### 日志位置
```
C:\NetWatchService\log.txt
```

### 日志示例
```
2024-01-15 08:30:01 - Service started - 开始检查网络状态
2024-01-15 08:30:01 - 第 1 次检查网络...
2024-01-15 08:30:01 - 网络异常 (连续失败: 1/3)
2024-01-15 08:31:01 - 第 2 次检查网络...
2024-01-15 08:31:02 - Ping 8.8.8.8 成功 - 延迟: 45ms
2024-01-15 08:31:02 - 网络正常 - 服务即将停止
2024-01-15 08:31:02 - Service stopped
```

---

## ??? 卸载服务

### 方法一：使用卸载脚本（推荐）

**右键点击 `uninstall.bat`，选择「以管理员身份运行」**

会提示是否删除安装文件和日志。

### 方法二：手动卸载

```powershell
# 停止服务
sc stop NetWatchService

# 删除服务
sc delete NetWatchService

# 删除文件（可选）
Remove-Item -Path "C:\NetWatchService" -Recurse -Force
```

---

## ?? 高级配置

### 修改检查参数

编辑 `NetworkMonitorService.cs`：

```csharp
private int maxCheckCount = 10;        // 最多检查次数（默认 10 次 = 10 分钟）
private int failureThreshold = 3;      // 连续失败阈值（默认 3 次）
private Timer timer = new Timer(60000); // 检查间隔（默认 60000ms = 1 分钟）
```

### 修改日志路径

```csharp
private string logPath = @"C:\NetWatchService\log.txt";
```

### 修改 Ping 目标

```csharp
PingReply reply = ping.Send("8.8.8.8", 3000);      // Google DNS
reply = ping.Send("114.114.114.114", 3000);        // 国内 DNS
```

修改后重新运行 `install.bat` 部署。

---

## ?? 故障排查

### 服务无法启动

1. 检查是否以管理员身份运行
2. 查看事件查看器：`Win + R` → `eventvwr.msc` → Windows 日志 → 应用程序
3. 检查日志文件：`C:\NetWatchService\log.txt`

### 服务启动后立即停止

可能是网络已经正常，这是预期行为。查看日志确认。

### 网络正常但服务不停止

1. 检查防火墙是否阻止 Ping
2. 检查网络是否真的能访问外网
3. 使用 `manage.bat` → 选项 8 手动测试网络

### 服务没有重启电脑

1. 确认连续失败达到 3 次
2. 检查是否有权限执行 shutdown 命令
3. 查看日志中的错误信息

---

## ?? 注意事项

1. **需要管理员权限**：服务安装、启动、重启电脑都需要管理员权限
2. **自动启动**：服务设置为「自动」启动，系统启动时会自动运行
3. **重启风险**：网络异常时会强制重启电脑，请确保无未保存数据
4. **日志管理**：日志会一直累积，建议定期清空
5. **防火墙**：确保允许 Ping 外网（ICMP 协议）

---

## ?? 常见问题

**Q: 断电后多久开始检查？**  
A: 系统启动后立即开始，第一次检查在服务启动时执行。

**Q: 网络正常后服务会一直运行吗？**  
A: 不会，网络正常后服务会自动停止，不占用资源。

**Q: 如何测试服务是否工作？**  
A: 使用 `manage.bat` 查看日志，或断开网线后启动服务观察行为。

**Q: 可以改成不重启电脑，只重启网卡吗？**  
A: 可以修改代码，将 `shutdown /r` 改为 `netsh interface set interface "网卡名称" disabled/enabled`。

---

## ?? 开源协议

本项目仅供学习和内部使用。

---

## 版本历史

- **v1.0** - 初始版本，基础网络监控和重启功能
- **v1.1** - 添加自动停止机制、防误判、多 DNS 检测
- **v1.2** - 添加一键部署脚本和管理工具

---

**开发者**: NetWatchService  
**最后更新**: 2024
