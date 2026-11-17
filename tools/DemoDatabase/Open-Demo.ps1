$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$build = Join-Path $root 'build_verify/demo-database/wpf'
& dotnet build (Join-Path $root 'SalesManagement_SysDev.sln') --no-restore -m:1 -v:minimal "-p:OutDir=$build" -p:UseAppHost=false
if ($LASTEXITCODE -ne 0) { throw 'The demo application build failed.' }
& dotnet (Join-Path $build 'SalesManagement_SysDev.dll') --demo-database
