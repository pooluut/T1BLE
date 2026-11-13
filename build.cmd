@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: Install .NET 10 SDK first: https://dotnet.microsoft.com/download/dotnet/10.0
    goto failed
)

echo Building Release and Debug EXEs without the .NET runtime...
echo If T1BLE is running, exit it from the tray before building.
dotnet publish "T1BLE.csproj" -c Release -p:PublishProfile=SingleFile --nologo
if errorlevel 1 goto failed

if not exist "dist\T1BLE.exe" (
    echo ERROR: Output EXE was not found.
    goto failed
)

dotnet publish "T1BLE.csproj" -c Debug -p:PublishProfile=SingleFile -p:PublishDir=dist/debug/ --nologo
if errorlevel 1 goto failed
if not exist "dist\debug\T1BLE.exe" goto failed

echo.
echo Build succeeded: "%~dp0dist\T1BLE.exe"
echo Debug with logs: "%~dp0dist\debug\T1BLE.exe"
echo Requires Microsoft .NET 10 Desktop Runtime x64 on the target PC.
popd
if /i not "%~1"=="--no-pause" pause
exit /b 0

:failed
echo.
echo Build failed. See the error above.
popd
if /i not "%~1"=="--no-pause" pause
exit /b 1
