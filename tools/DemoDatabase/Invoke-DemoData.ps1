param(
    [ValidateSet('seed','verify','smoke')][string]$Mode = 'seed',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$scratch = Join-Path $root 'build_verify/demo-database'
$reportDirectory = Join-Path $root 'docs/demo-database'
$private = Join-Path $scratch 'private'
$credentialFile = Join-Path $private 'demo-logins.clixml'
$build = Join-Path $scratch 'runner'

& git -C $root check-ignore --quiet -- 'build_verify/demo-database/private/demo-logins.clixml'
if ($LASTEXITCODE -ne 0) { throw 'The demo credential directory must be ignored by Git.' }
$marker = & sqlcmd -S '(LocalDB)\MSSQLLocalDB' -d SalesManagement_Demo -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'KansaiOADemoFixture';"
if ($LASTEXITCODE -ne 0 -or (($marker -join "`n").Trim()) -ne 'KansaiOA/v1') {
    throw 'The named demo database has not been provisioned. Run New-DemoSchema.ps1 first.'
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'DemoDatabase.csproj') -m:1 -v:minimal "-p:OutDir=$build" -p:UseAppHost=false -p:NuGetAudit=false -p:RestoreIgnoreFailedSources=true
    if ($LASTEXITCODE -ne 0) { throw 'The demo runner build failed.' }
}
$runner = Join-Path $build 'DemoDatabase.dll'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw 'The demo runner has not been built.' }

& (Join-Path $PSScriptRoot 'Protect-PrivateDirectory.ps1')
$lockPath = Join-Path $private 'demo-credentials.lock'
$lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    if (-not (Test-Path -LiteralPath $credentialFile -PathType Leaf)) {
        if ($Mode -ne 'seed') { throw 'The encrypted demo credentials are required for verification.' }
        $count = & sqlcmd -S '(LocalDB)\MSSQLLocalDB' -d SalesManagement_Demo -E -C -b -h -1 -W -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.M_Employee;'
        if ($LASTEXITCODE -ne 0 -or (($count -join "`n").Trim()) -ne '0') {
            throw 'Existing demo accounts require their original Windows-encrypted credential file.'
        }
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
        $byte = [byte[]]::new(1)
        $ceiling = 256 - (256 % $alphabet.Length)
        try {
            $credentials = foreach ($id in 9101..9115) {
                $secure = [Security.SecureString]::new()
                while ($secure.Length -lt 10) {
                    $random.GetBytes($byte)
                    if ($byte[0] -lt $ceiling) { $secure.AppendChar($alphabet[$byte[0] % $alphabet.Length]) }
                }
                $secure.MakeReadOnly()
                [Management.Automation.PSCredential]::new($id.ToString(), $secure)
            }
            # Export-Clixml protects these SecureStrings using Windows DPAPI for the current account.
            @($credentials) | Export-Clixml -LiteralPath $credentialFile -NoClobber
        }
        finally { $random.Dispose() }
    }

    $credentials = @(Import-Clixml -LiteralPath $credentialFile)
    $credentials | ForEach-Object { $_.UserName + "`t" + $_.GetNetworkCredential().Password } |
        & dotnet $runner --demo-database $Mode --report-dir $reportDirectory
    if ($LASTEXITCODE -ne 0) { throw "Demo $Mode failed. The original database is separate and the demo can be inspected or retried." }
}
finally { $lock.Dispose() }
