@echo off
chcp 936 >nul
setlocal enabledelayedexpansion

echo ========================================
echo   NetWatchService 卸载脚本
echo ========================================
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [错误] 请以管理员身份运行此脚本！
    echo 右键点击脚本，选择"以管理员身份运行"
    pause
    exit /b 1
)

set SERVICE_NAME=NetWatchService
set INSTALL_DIR=C:\NetWatchService
set CONFIG_DIR=%ProgramData%\NetWatchService

echo [1/3] 停止服务...
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    sc stop %SERVICE_NAME%
    timeout /t 3 /nobreak >nul
    echo [成功] 服务已停止
) else (
    echo [提示] 服务未运行或不存在
)

echo.
echo [2/3] 删除服务...
sc delete %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    echo [成功] 服务已删除
) else (
    echo [提示] 服务删除失败或不存在
)

echo.
echo [3/3] 是否删除安装文件、配置和日志？
echo 安装路径: %INSTALL_DIR%
echo 配置路径: %CONFIG_DIR%
echo.
choice /C YN /M "确认删除所有文件"

if %errorLevel% equ 1 (
    if exist "%INSTALL_DIR%" (
        rd /s /q "%INSTALL_DIR%"
        echo [成功] 安装文件已删除
    )
    if exist "%CONFIG_DIR%" (
        rd /s /q "%CONFIG_DIR%"
        echo [成功] 配置和日志已删除
    )
    echo [成功] 所有文件已删除
) else (
    echo [提示] 保留安装文件、配置和日志
)

echo.
echo ========================================
echo   卸载完成！
echo ========================================
echo.
pause
