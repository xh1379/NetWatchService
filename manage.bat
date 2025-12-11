@echo off
chcp 936 >nul

echo ========================================
echo   NetWatchService 快速操作
echo ========================================
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [错误] 请以管理员身份运行此脚本！
    pause
    exit /b 1
)

set SERVICE_NAME=NetWatchService
set CONFIG_DIR=%ProgramData%\NetWatchService
set LOG_PATH=%CONFIG_DIR%\log.txt
set CONFIG_PATH=%CONFIG_DIR%\settings.ini

:menu
cls
echo ========================================
echo   NetWatchService 服务管理
echo ========================================
echo.
echo 请选择操作:
echo.
echo   1. 启动服务
echo   2. 停止服务
echo   3. 重启服务
echo   4. 查看服务状态
echo   5. 查看日志（最后 20 行）
echo   6. 查看完整日志
echo   7. 清空日志
echo   8. 编辑配置文件
echo   9. 测试网络检查功能
echo   0. 退出
echo.
echo ========================================

choice /C 1234567890 /N /M "请输入选项 (0-9): "

if %errorLevel% equ 1 goto start_service
if %errorLevel% equ 2 goto stop_service
if %errorLevel% equ 3 goto restart_service
if %errorLevel% equ 4 goto status_service
if %errorLevel% equ 5 goto view_log_tail
if %errorLevel% equ 6 goto view_log_full
if %errorLevel% equ 7 goto clear_log
if %errorLevel% equ 8 goto edit_config
if %errorLevel% equ 9 goto test_network
if %errorLevel% equ 10 goto end

:start_service
echo.
echo [操作] 启动服务...
sc start %SERVICE_NAME%
echo.
pause
goto menu

:stop_service
echo.
echo [操作] 停止服务...
sc stop %SERVICE_NAME%
echo.
pause
goto menu

:restart_service
echo.
echo [操作] 重启服务...
sc stop %SERVICE_NAME%
timeout /t 2 /nobreak >nul
sc start %SERVICE_NAME%
echo.
pause
goto menu

:status_service
echo.
echo [操作] 查询服务状态...
echo.
sc query %SERVICE_NAME%
echo.
pause
goto menu

:view_log_tail
echo.
echo [操作] 最近 20 行日志...
echo ========================================
if exist "%LOG_PATH%" (
    powershell -Command "Get-Content '%LOG_PATH%' -Tail 20"
) else (
    echo 日志文件不存在: %LOG_PATH%
    echo 请检查配置文件中的 LogPath 设置
)
echo ========================================
echo.
pause
goto menu

:view_log_full
echo.
echo [操作] 打开完整日志...
if exist "%LOG_PATH%" (
    notepad "%LOG_PATH%"
) else (
    echo 日志文件不存在: %LOG_PATH%
    echo 请检查配置文件中的 LogPath 设置
    pause
)
goto menu

:clear_log
echo.
echo [警告] 确认要清空日志吗？
choice /C YN /M "确认清空"
if %errorLevel% equ 1 (
    if exist "%LOG_PATH%" (
        echo. > "%LOG_PATH%"
        echo [成功] 日志已清空
    ) else (
        echo [提示] 日志文件不存在
    )
    pause
)
goto menu

:edit_config
echo.
echo [操作] 打开配置文件编辑器...
if exist "%CONFIG_PATH%" (
    notepad "%CONFIG_PATH%"
    echo.
    echo [提示] 配置修改后需要重启服务才能生效
    echo.
    choice /C YN /M "是否现在重启服务"
    if %errorLevel% equ 1 (
        sc stop %SERVICE_NAME%
        timeout /t 2 /nobreak >nul
        sc start %SERVICE_NAME%
        echo [成功] 服务已重启
        pause
    )
) else (
    echo 配置文件不存在: %CONFIG_PATH%
    echo.
    echo 是否创建默认配置文件？
    choice /C YN /M "确认创建"
    if %errorLevel% equ 1 (
        if not exist "%CONFIG_DIR%" mkdir "%CONFIG_DIR%"
        (
            echo # NetWatchService 配置文件
            echo LogPath=%%ProgramData%%\NetWatchService\log.txt
            echo IntervalMs=60000
            echo PingTimeoutMs=3000
            echo MaxCheckCount=10
            echo FailureThreshold=3
            echo PingTargets=8.8.8.8,114.114.114.114
            echo TestMode=false
            echo AutoStopOnNetworkOk=true
        ) > "%CONFIG_PATH%"
        echo [成功] 默认配置文件已创建
        notepad "%CONFIG_PATH%"
    )
    pause
)
goto menu

:test_network
echo.
echo [操作] 手动测试网络连接...
echo ========================================
echo 测试 Google DNS (8.8.8.8)...
ping -n 1 -w 3000 8.8.8.8
echo.
echo 测试国内 DNS (114.114.114.114)...
ping -n 1 -w 3000 114.114.114.114
echo.
echo 检查网卡状态...
powershell -Command "Get-NetAdapter | Where-Object {$_.Status -eq 'Up' -and $_.InterfaceDescription -notlike '*Loopback*'} | Select-Object Name, InterfaceDescription, Status, LinkSpeed | Format-Table -AutoSize"
echo ========================================
echo.
pause
goto menu

:end
echo.
echo 再见！
timeout /t 1 /nobreak >nul
exit /b 0
