@echo off
cd /d "%~dp0"
dotnet run --project SalesManagement_SysDev -- --practice
if errorlevel 1 pause
