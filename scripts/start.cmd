@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

if exist "PalOps.Web.exe" (
    PalOps.Web.exe
    goto :end
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 未找到 PalOps.Web.exe，也未安装 dotnet。
    echo 请使用 publish-win-x64.ps1 -SelfContained 生成自包含版本，或安装 .NET 10 ASP.NET Core Runtime/SDK。
    pause
    exit /b 1
)

if exist "PalOps.Web.dll" (
    dotnet PalOps.Web.dll
    goto :end
)

if exist "%SCRIPT_DIR%..\src\PalOps.Web\PalOps.Web.csproj" (
    cd /d "%SCRIPT_DIR%.."
    dotnet run --project ".\src\PalOps.Web\PalOps.Web.csproj"
    goto :end
)

echo [ERROR] 未找到 PalOps.Web.exe、PalOps.Web.dll 或源码项目。
pause
exit /b 1

:end
if errorlevel 1 pause
endlocal
