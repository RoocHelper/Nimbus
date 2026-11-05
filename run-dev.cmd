@echo off
cd /d "%~dp0"
echo Membangun dan membuka Nimbus (mode uji)...
dotnet build src\Nimbus\Nimbus.csproj -c Debug -nologo -v q -clp:ErrorsOnly > build-log.txt 2>&1
if errorlevel 1 (type build-log.txt & echo. & echo Build gagal. Kirim isi build-log.txt ke Claude. & pause & exit /b 1)
start "" "src\Nimbus\bin\Debug\net8.0-windows\Nimbus.exe"
