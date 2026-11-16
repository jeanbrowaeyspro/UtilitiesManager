@echo off
rem Produit dist\UtilitiesManager.exe, seul fichier livré :
rem   lanceur .NET Framework 4.8 qui embarque l'application UtilitiesManager.App.exe
rem   (exe unique sans runtime embarqué ; le lanceur vérifie le runtime .NET Desktop 10).
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set APP=build\app
set LAUNCHER=build\launcher
if not exist assets\icon.ico (
    dotnet run build_tools\MakeIcon.cs || exit /b 1
)
if exist %APP% rmdir /s /q %APP%
if exist %LAUNCHER% rmdir /s /q %LAUNCHER%
if exist dist rmdir /s /q dist
dotnet publish src\UtilitiesManager -c Release -o %APP% || exit /b 1
dotnet publish src\UtilitiesManager.Launcher -c Release -o %LAUNCHER% "-p:AppExe=%CD%\%APP%\UtilitiesManager.App.exe" || exit /b 1
mkdir dist
copy /y %LAUNCHER%\UtilitiesManager.exe dist\ >nul || exit /b 1
rem Empreinte SHA-256 publiée à côté de l'exe : vérifiée par la mise à jour automatique.
powershell -NoProfile -Command "$h=(Get-FileHash dist\UtilitiesManager.exe -Algorithm SHA256).Hash.ToLower(); [IO.File]::WriteAllText('dist\UtilitiesManager.exe.sha256', $h + '  UtilitiesManager.exe')" || exit /b 1
echo.
echo Exécutable : dist\UtilitiesManager.exe
