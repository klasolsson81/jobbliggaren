$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$target = [IO.Path]::GetFullPath((Join-Path $tempRoot ('jbl-redis-policy-' + [Guid]::NewGuid().ToString('N'))))
if (-not $target.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path escaped temporary root.' }
try {
    & "$PSScriptRoot/prepare-dev-redis.ps1" -OutputDirectory $target | Out-Null
    $roles = @('api-persistent', 'api-volatile', 'worker-persistent', 'health-persistent', 'health-volatile', 'operator-persistent', 'operator-volatile')
    $passwords = @{}
    foreach ($role in $roles) {
        if ($role.StartsWith('health-')) {
            $passwords[$role] = [IO.File]::ReadAllText((Join-Path $target ($role.Substring(7) + '/health-password')))
        } elseif ($role.StartsWith('operator-')) {
            $passwords[$role] = [IO.File]::ReadAllText((Join-Path $target ('operator/' + $role.Substring(9) + '-password')))
        } else {
            $port = if ($role -eq 'api-volatile') { '6381' } else { '6379' }
            $connection = [IO.File]::ReadAllText((Join-Path $target "$role/connection"))
            $prefix = "127.0.0.1:$port,user=$role,password="
            if (-not $connection.StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Development role/endpoint differs.' }
            $passwords[$role] = $connection.Substring($prefix.Length)
        }
        if ($passwords[$role] -cnotmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid test credential encoding.' }
    }
    if (@($passwords.Values | Sort-Object -Unique).Count -ne 7) { throw 'Development credentials are not independent.' }
    foreach ($store in @('persistent', 'volatile')) {
        $expected = [IO.File]::ReadAllText("$PSScriptRoot/../deploy/redis/$store.acl.template").TrimEnd() + "`n" +
            [IO.File]::ReadAllText("$PSScriptRoot/../deploy/redis/operator-$store.acl.template").TrimEnd() + "`n"
        foreach ($role in $roles) {
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($passwords[$role]))).ToLowerInvariant()
            $expected = $expected.Replace('{{' + $role.ToUpperInvariant().Replace('-', '_') + '_SHA256}}', $hash)
            if ($role -eq "operator-$store") { $expected = $expected.Replace('{{OPERATOR_SHA256}}', $hash) }
        }
        $actual = [IO.File]::ReadAllText((Join-Path $target "$store/users.acl"))
        if ($expected.Contains('{{') -or $actual -cne $expected.Replace("`r`n", "`n")) { throw 'Development ACL differs from the shared store-specific policy.' }
    }
    Write-Output 'Development Redis identity and policy parity passed.'
} finally {
    if ((Test-Path -LiteralPath $target) -and [IO.Path]::GetFullPath($target).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
