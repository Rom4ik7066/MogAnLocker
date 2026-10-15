@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework C# compiler not found. Install .NET Framework 4.8 Developer Pack or Visual Studio Build Tools.
    exit /b 1
)

if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:winexe /out:"%~dp0bin\MogLockerApp.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "%~dp0MogLocker.cs" "%~dp0SecurityTools.cs" "%~dp0AnimatedButton.cs" "%~dp0AdBlock\AdBlockModels.cs" "%~dp0AdBlock\AdBlockStore.cs" "%~dp0AdBlock\FilterRuleEngine.cs" "%~dp0AdBlock\FilterListService.cs" "%~dp0AdBlock\NativeMessagingRegistration.cs" "%~dp0AdBlock\NativeMessagingHost.cs" "%~dp0AdBlock\AdBlockPanel.cs"
if errorlevel 1 exit /b %errorlevel%

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-extension.ps1" -SourceDirectory "%~dp0browser-extension" -ArchivePath "%~dp0bin\browser-extension.zip"
if errorlevel 1 exit /b %errorlevel%

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $source = Join-Path '%~dp0' 'soundpad'; if (-not (Test-Path -LiteralPath $source -PathType Container)) { $source = Join-Path '%~dp0..' 'soundpad' }; $target = Join-Path '%~dp0bin' 'soundpad.zip'; if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw \"Soundpad directory not found: $source\" }; if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }; Compress-Archive -Path $source -DestinationPath $target -Force; if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw \"Failed to create soundpad archive: $target\" }"
if errorlevel 1 exit /b %errorlevel%

set "PACKAGE_TMP=%~dp0bin\MogLocker.new.exe"
if exist "%PACKAGE_TMP%" del /q "%PACKAGE_TMP%"
"%CSC%" /nologo /target:winexe /out:"%PACKAGE_TMP%" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:"%~dp0bin\MogLockerApp.exe",MogLockerApp.exe /resource:"%~dp0bootstrap.ps1",bootstrap.ps1 /resource:"%~dp0bin\browser-extension.zip",browser-extension.zip /resource:"%~dp0bin\soundpad.zip",soundpad.zip "%~dp0Launcher.cs"
if errorlevel 1 (
    if exist "%PACKAGE_TMP%" del /q "%PACKAGE_TMP%"
    exit /b 1
)

move /Y "%PACKAGE_TMP%" "%~dp0bin\MogLocker.exe" >nul
if errorlevel 1 (
    if exist "%PACKAGE_TMP%" del /q "%PACKAGE_TMP%"
    echo Could not replace MogLocker.exe. Close any running copy and retry.
    exit /b 1
)

del /q "%~dp0bin\MogLockerApp.exe"
if errorlevel 1 exit /b %errorlevel%
echo Built: %~dp0bin\MogLocker.exe
