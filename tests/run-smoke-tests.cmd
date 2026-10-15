@echo off
setlocal
call "%~dp0..\build.cmd"
if errorlevel 1 exit /b %errorlevel%

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "TEST_DIR=%TEMP%\MogLocker-AdBlock-Smoke-%RANDOM%-%RANDOM%"
mkdir "%TEST_DIR%"
call "%CSC%" /nologo /target:winexe /out:"%TEST_DIR%\MogLockerApp.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "%~dp0..\MogLocker.cs" "%~dp0..\SecurityTools.cs" "%~dp0..\AnimatedButton.cs" "%~dp0..\AdBlock\AdBlockModels.cs" "%~dp0..\AdBlock\AdBlockStore.cs" "%~dp0..\AdBlock\FilterRuleEngine.cs" "%~dp0..\AdBlock\FilterListService.cs" "%~dp0..\AdBlock\NativeMessagingRegistration.cs" "%~dp0..\AdBlock\NativeMessagingHost.cs" "%~dp0..\AdBlock\AdBlockPanel.cs"
if errorlevel 1 (
    rd /s /q "%TEST_DIR%"
    exit /b 1
)
"%CSC%" /nologo /target:exe /out:"%TEST_DIR%\AdBlockEngineSmokeTests.exe" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:"%TEST_DIR%\MogLockerApp.exe" "%~dp0AdBlockEngineSmokeTests.cs"
if errorlevel 1 (
    rd /s /q "%TEST_DIR%"
    exit /b 1
)

"%TEST_DIR%\AdBlockEngineSmokeTests.exe"
set "RESULT=%errorlevel%"
rd /s /q "%TEST_DIR%"
exit /b %RESULT%
