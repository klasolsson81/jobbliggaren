param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../.redis-dev'))
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $target) {
    throw 'Redis development credentials already exist. Preserve them; credential rotation requires its own procedure.'
}
$roles = @('api-persistent', 'api-volatile', 'worker-persistent', 'health-persistent', 'health-volatile', 'operator-persistent', 'operator-volatile')
$passwords = @{}
foreach ($role in $roles) { $passwords[$role] = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) }
$stage = $target + '.staging-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($stage) | Out-Null
if ($IsWindows) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $stage /inheritance:r /grant:r "*${sid}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not restrict the development credential directory.' }
} else {
    [IO.File]::SetUnixFileMode($stage, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
}
function Write-PrivateFile([string]$RelativePath, [string]$Content) {
    $path = Join-Path $stage $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
}
foreach ($store in @('persistent', 'volatile')) {
    $policy = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "../deploy/redis/$store.acl.template"))
    foreach ($role in $roles) {
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($passwords[$role]))).ToLowerInvariant()
        $policy = $policy.Replace('{{' + $role.ToUpperInvariant().Replace('-', '_') + '_SHA256}}', $hash)
    }
    $operator = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "../deploy/redis/operator-$store.acl.template"))
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($passwords["operator-$store"]))).ToLowerInvariant()
    $operator = $operator.Replace('{{OPERATOR_SHA256}}', $hash)
    $policy = ($policy.TrimEnd() + "`n" + $operator.TrimEnd() + "`n").Replace("`r`n", "`n")
    if ($policy.Contains('{{') -or $policy.Contains('}}')) { throw 'Unresolved Redis policy placeholder.' }
    Write-PrivateFile "$store/users.acl" $policy
    Write-PrivateFile "$store/health-password" $passwords["health-$store"]
    Write-PrivateFile "operator/$store-password" $passwords["operator-$store"]
}
foreach ($role in @('api-persistent', 'api-volatile', 'worker-persistent')) {
    $port = if ($role -eq 'api-volatile') { 6381 } else { 6379 }
    Write-PrivateFile "$role/connection" "localhost:$port,user=$role,password=$($passwords[$role])"
}
[IO.Directory]::Move($stage, $target)
Write-Output "Created private development Redis files in $target. No service was started."
