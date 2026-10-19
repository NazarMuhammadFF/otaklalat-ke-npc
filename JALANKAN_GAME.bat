@echo off
title Simulasi Otak Lalat NPC - Godot 4.7 Mono
cd /d "%~dp0"

echo ========================================================
echo   Menjalankan Prototype Otak Lalat NPC (Godot Mono)
echo ========================================================
echo.

set "GODOT_EXE=D:\Ruang game\Godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"

if not exist "%GODOT_EXE%" (
    echo [ERROR] File executable Godot Mono tidak ditemukan di:
    echo "%GODOT_EXE%"
    echo Silakan periksa lokasi instalasi Godot Mono Anda.
    pause
    exit /b 1
)

start "" "%GODOT_EXE%" --path "%~dp0."
