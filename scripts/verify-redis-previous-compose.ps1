param(
    [string]$PreviousRef = 'origin/main',
    [string]$ApiImage = 'jbl-1759-api:pr2',
    [string]$WorkerImage = 'jbl-1759-worker:pr2'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previous = & git -C $repo show "${PreviousRef}:deploy/docker-compose.yml"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read previous Compose' }
$redisLines = @($previous | Select-String '^  ConnectionStrings__Redis:')
$volatileLines = @($previous | Select-String '^      ConnectionStrings__VolatileRedis:')
if ($redisLines.Count -ne 1 -or $volatileLines.Count -ne 1) { throw 'Previous Compose shape changed; re-evaluate compatibility' }
$redis = ($redisLines[0].Line -split ': ',2)[1].Trim('"')
$volatile = ($volatileLines[0].Line -split ': ',2)[1].Trim('"')
if ($redis -ne 'redis:6379' -or $volatile -ne 'redis-volatile:6379') { throw 'Previous Compose is no longer anonymous; re-evaluate compatibility' }
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
foreach ($role in @('api','worker')) {
    $image = if ($role -eq 'api') { $ApiImage } else { $WorkerImage }
    $arguments = @('run','--rm','--network','none',
        '-e',"ConnectionStrings__Redis=$redis",'-e',"ConnectionStrings__VolatileRedis=$volatile",
        '-e','ConnectionStrings__Postgres=Host=unused;Database=fixture;Username=fixture;Password=fixture-only',
        '-e','DOTNET_ENVIRONMENT=Test','-e','ASPNETCORE_ENVIRONMENT=Test',
        '-e',"FieldEncryption__LocalMasterKeyBase64=$key",'-e',"AuditPseudonymization__PepperBase64=$key",
        '-e',"CompanyWatchPseudonymization__PepperBase64=$key",'-e',"CvReviewFingerprintPseudonymization__PepperBase64=$key",$image)
    $output = & docker @arguments 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch "Redis configuration for '$role-persistent' is invalid") {
        throw "Unexpected previous-Compose behavior for $role"
    }
    Write-Output "PASS measured incompatibility: $role refuses previous Compose anonymous Redis configuration"
}
