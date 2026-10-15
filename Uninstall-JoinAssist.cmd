@echo off
setlocal
title Uninstall JoinAssist
set "JoinAssistInstaller=%ProgramFiles%\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
if exist "%JoinAssistInstaller%" goto launch
set "JoinAssistInstaller=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\resources\app\ServiceHub\Services\Microsoft.VisualStudio.Setup.Service\VsixInstaller\VSIXInstaller.exe"
if exist "%JoinAssistInstaller%" goto launch
echo Microsoft VSIX Installer was not found. See docs/TROUBLESHOOTING.md.
pause
exit /b 2
:launch
echo Save your queries and close every SSMS window before confirming removal.
"%JoinAssistInstaller%" /uninstall:JoinAssist.FK.4d45475a-842b-42b3-b2f7-b392df87bdb8
if errorlevel 1 goto failed
exit /b 0
:failed
echo The Microsoft installer reported a failure or removal was cancelled.
echo Keep the installer error or log for troubleshooting.
pause
exit /b 1
