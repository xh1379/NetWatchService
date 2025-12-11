@echo off
chcp 936 >nul
setlocal enabledelayedexpansion

echo ========================================
echo   NetWatchService 一键部署脚本
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

echo [1/7] 检查环境...

:: 设置变量
set SERVICE_NAME=NetWatchService
set SERVICE_DISPLAY_NAME=网络监控服务
set SERVICE_DESC=断电恢复后自动检查网络状态，异常时重启计算机
set PROJECT_DIR=%~dp0
set BUILD_CONFIG=Release
set EXE_NAME=NetWatchService.exe
set INSTALL_DIR=C:\NetWatchService
set CONFIG_DIR=%ProgramData%\NetWatchService

:: 查找 MSBuild
set MSBUILD_PATH=
for %%v in (
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
) do (
    if exist %%v (
        set MSBUILD_PATH=%%v
        goto :msbuild_found
    )
)

echo [错误] 未找到 MSBuild.exe
echo 请确保已安装 Visual Studio 或 .NET Framework SDK
pause
exit /b 1

:msbuild_found
echo [成功] 找到 MSBuild: !MSBUILD_PATH!

:: 停止并删除旧服务
echo.
echo [2/7] 检查并停止旧服务...
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    echo [提示] 检测到已安装的服务，正在停止...
    sc stop %SERVICE_NAME% >nul 2>&1
    timeout /t 2 /nobreak >nul
    
    echo [提示] 正在删除旧服务...
    sc delete %SERVICE_NAME% >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo [成功] 旧服务已删除
) else (
    echo [成功] 未检测到旧服务
)

:: 编译项目
echo.
echo [3/7] 编译项目 (Release 模式)...
cd /d "%PROJECT_DIR%"
"!MSBUILD_PATH!" NetWatchService.csproj /p:Configuration=%BUILD_CONFIG% /t:Rebuild /v:minimal /nologo

if %errorLevel% neq 0 (
    echo [错误] 编译失败！
    pause
    exit /b 1
)
echo [成功] 编译成功

:: 查找生成的 exe
set EXE_PATH=%PROJECT_DIR%bin\%BUILD_CONFIG%\%EXE_NAME%
if not exist "%EXE_PATH%" (
    echo [错误] 未找到编译后的文件: %EXE_PATH%
    pause
    exit /b 1
)

:: 创建安装目录
echo.
echo [4/7] 准备安装目录...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
)
if not exist "%CONFIG_DIR%" (
    mkdir "%CONFIG_DIR%"
)

:: 复制文件
echo [提示] 复制文件到 %INSTALL_DIR%...
copy /Y "%EXE_PATH%" "%INSTALL_DIR%\" >nul
copy /Y "%PROJECT_DIR%bin\%BUILD_CONFIG%\*.dll" "%INSTALL_DIR%\" >nul 2>&1
copy /Y "%PROJECT_DIR%bin\%BUILD_CONFIG%\*.config" "%INSTALL_DIR%\" >nul 2>&1
echo [成功] 文件复制完成

:: 部署配置文件
echo.
echo [5/7] 部署配置文件...
if exist "%PROJECT_DIR%settings.ini" (
    if not exist "%CONFIG_DIR%\settings.ini" (
        copy /Y "%PROJECT_DIR%settings.ini" "%CONFIG_DIR%\" >nul
        echo [成功] 配置文件已部署到: %CONFIG_DIR%\settings.ini
    ) else (
        echo [提示] 配置文件已存在，保留现有配置
        echo [提示] 如需重置配置，请删除 %CONFIG_DIR%\settings.ini 后重新运行
    )
) else (
    echo [提示] 未找到 settings.ini，将使用默认配置
)

:: 安装服务
echo.
echo [6/7] 安装服务...
sc create %SERVICE_NAME% binPath= "%INSTALL_DIR%\%EXE_NAME%" start= auto DisplayName= "%SERVICE_DISPLAY_NAME%"
if %errorLevel% neq 0 (
    echo [错误] 服务安装失败！
    pause
    exit /b 1
)

sc description %SERVICE_NAME% "%SERVICE_DESC%"
sc failure %SERVICE_NAME% reset= 86400 actions= restart/60000/restart/60000/restart/60000

echo [成功] 服务安装成功

:: 启动服务
echo.
echo [7/7] 启动服务...
sc start %SERVICE_NAME%
if %errorLevel% neq 0 (
    echo [警告] 服务启动失败，请手动检查
) else (
    echo [成功] 服务启动成功
)

:: 显示服务状态
echo.
echo ========================================
echo   部署完成！
echo ========================================
echo.
echo 服务名称: %SERVICE_NAME%
echo 显示名称: %SERVICE_DISPLAY_NAME%
echo 安装路径: %INSTALL_DIR%\%EXE_NAME%
echo 配置文件: %CONFIG_DIR%\settings.ini
echo 日志路径: %CONFIG_DIR%\log.txt (默认，可在配置文件中修改)
echo 启动类型: 自动（系统启动时运行）
echo.
echo ========================================
echo   服务状态
echo ========================================
sc query %SERVICE_NAME%
echo.
echo ========================================
echo   常用命令
echo ========================================
echo 查看日志: type %CONFIG_DIR%\log.txt
echo 编辑配置: notepad %CONFIG_DIR%\settings.ini
echo 停止服务: sc stop %SERVICE_NAME%
echo 启动服务: sc start %SERVICE_NAME%
echo 卸载服务: 运行 uninstall.bat
echo 管理服务: 运行 manage.bat
echo ========================================
echo.
pause
