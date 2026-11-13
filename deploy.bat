@echo off
REM Script di deploy: copia i file del tool in una cartella di esecuzione dedicata
REM Eseguire dopo git pull dalla cartella genshin-tool

SET DEST=D:\GenshinKamera

echo Creo cartella di esecuzione: %DEST%
if not exist "%DEST%" mkdir "%DEST%"

echo Copio i file...
xcopy /Y /E /I "%~dp0tessdata" "%DEST%\tessdata\"
xcopy /Y /E /I "%~dp0inventorylists" "%DEST%\inventorylists\"
xcopy /Y /E /I "%~dp0x64" "%DEST%\x64\"
copy /Y "%~dp0*.exe" "%DEST%\"
copy /Y "%~dp0*.config" "%DEST%\"
copy /Y "%~dp0*.dll" "%DEST%\"
copy /Y "%~dp0NLog.config" "%DEST%\" 2>nul

echo.
echo Deploy completato in %DEST%
echo Avvia InventoryKamera.exe da: %DEST%
pause
