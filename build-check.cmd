@echo off
cd /d "%~dp0"
echo Membangun Nimbus... (log: build-log.txt)
echo === SDK === > build-log.txt
dotnet --list-sdks >> build-log.txt 2>&1
echo === BUILD === >> build-log.txt
dotnet build src\Nimbus\Nimbus.csproj -c Debug -nologo -clp:ErrorsOnly >> build-log.txt 2>&1
echo EXITCODE=%ERRORLEVEL% >> build-log.txt
type build-log.txt
echo.
echo Selesai. Kamu boleh menutup jendela ini.
pause
