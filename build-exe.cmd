@echo off
cd /d "%~dp0"
echo Membuat Nimbus.exe (satu file, tanpa perlu install .NET)...
if exist dist rmdir /s /q dist
dotnet publish src\Nimbus\Nimbus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -nologo -o dist > build-exe-log.txt 2>&1
echo EXITCODE=%ERRORLEVEL% >> build-exe-log.txt
if exist dist\Nimbus.exe (echo. & echo Berhasil: dist\Nimbus.exe & explorer dist) else (echo. & echo Gagal, lihat build-exe-log.txt & type build-exe-log.txt)
pause
