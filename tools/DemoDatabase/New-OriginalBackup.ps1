# Creates a copy-only backup and verifies it on this LocalDB instance.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$private = Join-Path $root 'build_verify/demo-database/private/backups'
& git -C $root check-ignore --quiet -- 'build_verify/demo-database/private/backups/test.bak'
if ($LASTEXITCODE -ne 0) { throw 'The backup directory must be ignored by Git.' }
& (Join-Path $PSScriptRoot 'Protect-PrivateDirectory.ps1')
New-Item -ItemType Directory -Path $private -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$file = Join-Path $private "SalesManagement-$stamp.bak"
if (Test-Path -LiteralPath $file) { throw 'Backup path already exists.' }
$sqlPath = $file.Replace("'", "''")
$query = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'SalesManagement' THROW 51000, N'Unexpected source database.', 1;
BACKUP DATABASE [SalesManagement] TO DISK = N'$sqlPath' WITH COPY_ONLY, CHECKSUM, NOFORMAT, NOINIT;
RESTORE VERIFYONLY FROM DISK = N'$sqlPath' WITH CHECKSUM;
"@
& sqlcmd -S '(LocalDB)\MSSQLLocalDB' -d SalesManagement -E -C -b -W -Q $query
if ($LASTEXITCODE -ne 0) { throw 'Backup creation or RESTORE VERIFYONLY failed. Keep using the existing database.' }
$evidence = [ordered]@{
    Database = 'SalesManagement'
    CreatedUtc = $stamp
    Path = $file
    Bytes = (Get-Item -LiteralPath $file).Length
    SHA256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    CopyOnly = $true
    VerifiedBy = 'RESTORE VERIFYONLY WITH CHECKSUM'
}
$report = Join-Path $private "SalesManagement-$stamp.json"
$evidence | ConvertTo-Json | Set-Content -LiteralPath $report -Encoding UTF8
$evidence | ConvertTo-Json
