@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
    echo Instale o .NET 8 SDK para testar o SharkBabyDrivers a partir do codigo.
    pause
    exit /b 1
)
echo Compilando a versao atual do SharkBabyDrivers, sem instalar...
dotnet build "%~dp0src\SharkBabyDrivers\SharkBabyDrivers.csproj" -c Release -m:1
if errorlevel 1 (
    echo Nao foi possivel compilar. Confira a mensagem acima.
    pause
    exit /b 1
)
start "" "%~dp0src\SharkBabyDrivers\bin\Release\net8.0-windows\SharkBabyDrivers.exe"
