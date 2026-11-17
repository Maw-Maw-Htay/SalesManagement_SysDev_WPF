param([ValidateRange(9101, 9115)][int]$EmployeeId = 9101)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$credentialFile = Join-Path $root 'build_verify/demo-database/private/demo-logins.clixml'
$credential = Import-Clixml -LiteralPath $credentialFile | Where-Object UserName -EQ ([string]$EmployeeId) | Select-Object -First 1
if ($null -eq $credential) { throw 'The demo account credentials are unavailable for this Windows user.' }
Set-Clipboard -Value $credential.GetNetworkCredential().Password
Write-Output "Copied the password for demo employee $EmployeeId."
