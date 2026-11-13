@echo off
rem ---------------------------------------------------------------------------
rem  publish.bat - builds a Pixel8 release: tests, the self-contained game and
rem  artifacts\Pixel8-Setup-<version>.exe. Needs the .NET 8 SDK and Inno Setup 6.
rem
rem  Passes any arguments on to build\publish.ps1, for example:
rem    publish.bat -SkipTests
rem    publish.bat -InnoCompiler "D:\Tools\Inno Setup 6\ISCC.exe"
rem    publish.bat -Sign -CertThumbprint <thumbprint>
rem ---------------------------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish FAILED - see the messages above.
)
pause
