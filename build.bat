@echo off
rem 编译 JsonRightClick.exe（只需要 Windows 自带的 .NET Framework 4.x，不用装 Visual Studio）
rem 同时编译 JsonRightClickMenu.exe：Win11 第一层右键菜单用的 COM 服务（带稀疏包身份；托盘不带，避免被系统结束）
cd /d "%~dp0"
if "%OUT%"=="" set OUT=JsonRightClick.exe
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
  echo csc.exe not found: .NET Framework 4.x is required
  if not defined CI pause
  exit /b 1
)
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:Accessibility.dll /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll"
set COMMON=/nologo /target:winexe /platform:anycpu /optimize+ /unsafe /codepage:65001
set ICON=
if exist app.ico set ICON=/win32icon:app.ico
"%FW%\csc.exe" %COMMON% /out:%OUT% %ICON% /win32manifest:app.manifest %REFS% *.cs
if errorlevel 1 ((if not defined CI pause) & exit /b 1)
if /i "%OUT%"=="JsonRightClick.exe" (
  "%FW%\csc.exe" %COMMON% /out:JsonRightClickMenu.exe %ICON% /win32manifest:appmenu.manifest %REFS% *.cs
  if errorlevel 1 ((if not defined CI pause) & exit /b 1)
)
