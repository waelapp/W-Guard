@echo off
chcp 65001 >nul
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.8 is required.
    exit /b 1
)
echo [1/3] Building W-Guard.exe ...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /langversion:5 ^
 /win32icon:app.ico /win32manifest:app.manifest /out:W-Guard.exe ^
 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
 /r:System.Management.dll /r:Microsoft.VisualBasic.dll /r:Microsoft.CSharp.dll ^
 /r:"%REF%\System.Web.Extensions.dll" /r:System.ServiceProcess.dll ^
 src\*.cs > build.log 2>&1
if errorlevel 1 (
  echo.
  echo BUILD FAILED - details in build.log
  type build.log
    exit /b 1
)
powershell -NoProfile -Command "$h=(Get-FileHash -LiteralPath 'W-Guard.exe' -Algorithm SHA256).Hash.ToLower(); ($h+'  W-Guard.exe') | Out-File -Encoding ascii 'W-Guard.exe.sha256'"
echo       OK - W-Guard.exe

echo [2/3] Looking for Inno Setup ...
set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe
if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe
if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe
if "%ISCC%"=="" (
  echo       Inno Setup is not installed - skipped the installer.
  echo       Get it free from https://jrsoftware.org/isdl.php then run build.bat again.
  goto done
)
echo [3/3] Building W-Guard-Setup.exe ...
"%ISCC%" /Q installer\setup.iss >> build.log 2>&1
if errorlevel 1 (
  echo SETUP BUILD FAILED - details in build.log
  type build.log
    exit /b 1
)
powershell -NoProfile -Command "$h=(Get-FileHash -LiteralPath 'W-Guard-Setup.exe' -Algorithm SHA256).Hash.ToLower(); ($h+'  W-Guard-Setup.exe') | Out-File -Encoding ascii 'W-Guard-Setup.exe.sha256'"
echo       OK - W-Guard-Setup.exe

:done
echo.
echo BUILD OK
echo Upload to the GitHub release: W-Guard-Setup.exe, W-Guard-Setup.exe.sha256, W-Guard.exe, W-Guard.exe.sha256
echo Build OK >> build.log
