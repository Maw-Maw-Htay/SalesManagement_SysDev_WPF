# The private subtree contains the original backup and the Windows-encrypted demo credentials.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$private = Join-Path $root 'build_verify/demo-database/private'
& git -C $root check-ignore --quiet -- 'build_verify/demo-database/private/demo-logins.clixml'
if ($LASTEXITCODE -ne 0) { throw 'The private demo directory must be ignored by Git.' }
New-Item -ItemType Directory -Path $private -Force | Out-Null
$directory = Get-Item -LiteralPath $private
if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The private demo directory cannot be a link.' }

$account = [Security.Principal.WindowsIdentity]::GetCurrent().User
$allowed = @($account, [Security.Principal.SecurityIdentifier]::new('S-1-5-18'), [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
$acl = Get-Acl -LiteralPath $private
$existing = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
if ($acl.AreAccessRulesProtected -and $existing.Count -eq $allowed.Count -and
    @($existing | Where-Object { $_.IdentityReference -notin $allowed }).Count -eq 0) { return }
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
    $acl.RemoveAccessRuleSpecific($rule)
}
$inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
foreach ($sid in $allowed) {
    $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', $inheritance, 'None', 'Allow')
    $acl.SetAccessRule($rule)
}
Set-Acl -LiteralPath $private -AclObject $acl
