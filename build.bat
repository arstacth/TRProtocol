@echo off
cd /d "%~dp0"
dotnet publish "%~dp0TRProtocol.csproj" -c Release -r win-x64
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo.
echo Built:
echo %~dp0bin\Release\net8.0-windows\win-x64\publish\TRProtocol.exe
