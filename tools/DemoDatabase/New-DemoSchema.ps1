param([string]$SqlPackagePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$instance = '(LocalDB)\MSSQLLocalDB'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path $root 'build_verify/demo-database'
$target = 'SalesManagement_Demo'

function Query([string]$database, [string]$sql) {
    $output = & sqlcmd -S $instance -d $database -E -C -b -h -1 -W -Q "SET NOCOUNT ON; $sql"
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
    return (($output -join "`n").Trim())
}

if ((Query 'master' "SELECT IIF(DB_ID(N'SalesManagement_Demo') IS NULL, 0, 1);") -eq '1') {
    $marker = Query $target "SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'KansaiOADemoFixture';"
    if ($marker -ne 'KansaiOA/v1') { throw 'SalesManagement_Demo already exists without the fixture marker. Inspect it before making changes.' }
    Write-Output 'The approved demo schema already exists. Existing rows and schema were left in place.'
    return
}

if (-not $SqlPackagePath) {
    $tool = Get-Command sqlpackage -ErrorAction SilentlyContinue
    if ($tool) { $SqlPackagePath = $tool.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $installation = & $vswhere -latest -products '*' -property installationPath | Select-Object -First 1
            if ($installation) { $SqlPackagePath = Join-Path $installation 'Common7/IDE/Extensions/Microsoft/SQLDB/DAC/SqlPackage.exe' }
        }
    }
}
if (-not $SqlPackagePath -or -not (Test-Path -LiteralPath $SqlPackagePath -PathType Leaf)) {
    throw 'SqlPackage is required. Pass its executable path with -SqlPackagePath.'
}
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$package = Join-Path $scratch "SalesManagement-schema-$stamp.dacpac"
& $SqlPackagePath /Action:Extract "/SourceServerName:$instance" /SourceDatabaseName:SalesManagement `
    /SourceTrustServerCertificate:True "/TargetFile:$package" /p:ExtractAllTableData=False `
    /p:IgnorePermissions=True /p:IgnoreUserLoginMappings=True /p:ExtractReferencedServerScopedElements=False /p:VerifyExtraction=True
if ($LASTEXITCODE -ne 0) { throw 'Extracting the source schema failed.' }

$collation = Query 'SalesManagement' 'SELECT CONVERT(nvarchar(128),DATABASEPROPERTYEX(DB_NAME(),N''Collation''));'
if ($collation -notmatch '^[A-Za-z0-9_]+$') { throw 'Unexpected source collation.' }
Query 'master' "IF DB_ID(N'SalesManagement_Demo') IS NOT NULL THROW 51000,N'Demo target already exists.',1; CREATE DATABASE [SalesManagement_Demo] COLLATE $collation;" | Out-Null
& $SqlPackagePath /Action:Publish "/SourceFile:$package" "/TargetServerName:$instance" "/TargetDatabaseName:$target" `
    /TargetTrustServerCertificate:True /p:CreateNewDatabase=False /p:BlockOnPossibleDataLoss=True `
    /p:DropObjectsNotInSource=False '/p:ExcludeObjectTypes=Users;Logins;Permissions;RoleMembership;DatabaseRoles'
if ($LASTEXITCODE -ne 0) { throw 'Publishing the schema failed. The newly created database was retained for inspection.' }

Query $target @'
IF DB_NAME() <> N'SalesManagement_Demo' THROW 51000,N'Invalid demo target.',1;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0) <> 27 THROW 51001,N'Expected 27 application tables.',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys) <> 50 THROW 51001,N'Expected 50 foreign keys.',1;
DECLARE @anchor nvarchar(10) = CONVERT(nvarchar(10),CONVERT(date,SWITCHOFFSET(SYSDATETIMEOFFSET(),'+09:00')),23);
EXEC sys.sp_addextendedproperty @name=N'KansaiOADemoFixture', @value=N'KansaiOA/v1';
EXEC sys.sp_addextendedproperty @name=N'KansaiOADemoAnchor', @value=@anchor;
SELECT N'SalesManagement_Demo was provisioned from the schema-only package.';
'@
