param([string]$WorkerImage = 'jbl-1759-worker:pr2', [string]$ApiImage = 'jbl-1759-api:pr2')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = 'jbl-1759-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$network = $runId + '-network'
$redis = $runId + '-redis'
$postgres = $runId + '-postgres'
$worker = $runId + '-worker'
$api = $runId + '-api'
$volatile = $runId + '-volatile'
$private = Join-Path $repo ('.redis-dev.staging-' + $runId)
$containers = @()
function Invoke-TestDocker([string[]]$Arguments) {
    $output = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed: $($Arguments[0]) (output retained privately by caller)" }
    return $output
}
function Probe {
    & docker exec $worker dotnet Jobbliggaren.Worker.dll --readiness-probe *> $null
    return $LASTEXITCODE
}
try {
    & (Join-Path $PSScriptRoot 'prepare-dev-redis.ps1') -OutputDirectory $private | Out-Null
    $connection = [IO.File]::ReadAllText((Join-Path $private 'worker-persistent/connection')).Replace('127.0.0.1:6379', 'redis:6379')
    [IO.File]::WriteAllText((Join-Path $private 'worker-persistent/connection'), $connection)
    foreach ($role in @('api-persistent', 'api-volatile')) {
        $endpoint = if ($role -eq 'api-volatile') { 'redis-volatile:6379' } else { 'redis:6379' }
        $path = Join-Path $private "$role/connection"
        $value = [IO.File]::ReadAllText($path) -replace '^127\.0\.0\.1:[0-9]+', $endpoint
        [IO.File]::WriteAllText($path, $value)
    }
    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $environment = @(
        'DOTNET_ENVIRONMENT=Test',
        'ASPNETCORE_ENVIRONMENT=Test',
        'ConnectionStrings__Postgres=Host=postgres;Database=jobbliggaren;Username=postgres;Password=fixture-only',
        'ConnectionStrings__Redis_FILE=/run/redis-persistent/connection',
        "FieldEncryption__LocalMasterKeyBase64=$key",
        "AuditPseudonymization__PepperBase64=$key",
        "CompanyWatchPseudonymization__PepperBase64=$key",
        "CvReviewFingerprintPseudonymization__PepperBase64=$key",
        'Email__Provider=Console',
        'Seq__ServerUrl=',
        'Logging__LogLevel__Default=Warning'
    )
    $environment | Set-Content (Join-Path $private 'worker.env')
    Invoke-TestDocker -Arguments @('network','create','--internal',$network) | Out-Null
    Invoke-TestDocker -Arguments @('run','-d','--name',$redis,'--network',$network,'--network-alias','redis','--mount',"type=bind,source=$private/persistent,target=/run/redis-policy,readonly",'redis:8.6-alpine','redis-server','--aclfile','/run/redis-policy/users.acl','--save','','--appendonly','no') | Out-Null
    $containers += $redis
    Invoke-TestDocker -Arguments @('run','-d','--name',$volatile,'--network',$network,'--network-alias','redis-volatile','--read-only','--tmpfs','/data:size=16m','--mount',"type=bind,source=$private/volatile,target=/run/redis-policy,readonly",'redis:8.6-alpine','redis-server','--aclfile','/run/redis-policy/users.acl','--save','','--appendonly','no') | Out-Null
    $containers += $volatile
    Invoke-TestDocker -Arguments @('run','-d','--name',$postgres,'--network',$network,'--network-alias','postgres','-e','POSTGRES_PASSWORD=fixture-only','-e','POSTGRES_DB=jobbliggaren','--tmpfs','/var/lib/postgresql','postgres:18') | Out-Null
    $containers += $postgres
    for ($i=0; $i -lt 60; $i++) {
        & docker exec $postgres pg_isready -U postgres -d jobbliggaren *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 500
    }
    Invoke-TestDocker -Arguments @('run','-d','--name',$worker,'--network',$network,'--read-only','--tmpfs','/tmp:size=16m,mode=1777','--cap-drop','ALL','--security-opt','no-new-privileges','--env-file',"$private/worker.env",'--mount',"type=bind,source=$private/worker-persistent,target=/run/redis-persistent,readonly",$WorkerImage) | Out-Null
    $containers += $worker
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        if ((Probe) -eq 0) { $ready=$true; break }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) {
        & docker logs $worker *> (Join-Path $private 'worker-startup.log')
        throw "Worker did not become ready; private log: $private/worker-startup.log"
    }
    $uid = (Invoke-TestDocker -Arguments @('exec',$worker,'id','-u')).Trim()
    if ($uid -eq '0') { throw 'Worker runs as root' }
    Write-Output 'PASS non-root, read-only Worker starts and answers through its own Redis connection'
    Invoke-TestDocker -Arguments @('kill','--signal','STOP',$worker) | Out-Null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if ((Probe) -eq 0 -or $watch.Elapsed.TotalSeconds -gt 8) { throw 'Suspended Worker incorrectly healthy or probe unbounded' }
    Invoke-TestDocker -Arguments @('kill','--signal','CONT',$worker) | Out-Null
    Write-Output 'PASS suspended Worker refuses within deadline'
    Invoke-TestDocker -Arguments @('stop',$redis) | Out-Null
    if ((Probe) -eq 0) { throw 'Worker ignored Redis outage' }
    Write-Output 'PASS live Worker reports Redis outage'
    Invoke-TestDocker -Arguments @('start',$redis) | Out-Null
    $ready=$false
    for ($i=0; $i -lt 30; $i++) { if ((Probe) -eq 0) { $ready=$true; break }; Start-Sleep -Milliseconds 500 }
    if (!$ready) { throw 'Worker failed to reconnect' }
    Write-Output 'PASS Worker recovers on same connection after Redis restart'
    Invoke-TestDocker -Arguments @('restart',$worker) | Out-Null
    $ready=$false
    for ($i=0; $i -lt 30; $i++) { if ((Probe) -eq 0) { $ready=$true; break }; Start-Sleep -Milliseconds 500 }
    if (!$ready) { throw 'Worker failed after restart' }
    Write-Output 'PASS Worker restart recreates its private readiness socket'
    $apiEnvironment = $environment + @(
        'ConnectionStrings__VolatileRedis_FILE=/run/redis-volatile/connection',
        'ForwardedHeaders__KnownNetworks__0=127.0.0.0/8',
        'ReverseProxy__HttpsEnabled=false'
    )
    $apiEnvironment | Set-Content (Join-Path $private 'api.env')
    Invoke-TestDocker -Arguments @('run','-d','--name',$api,'--network',$network,'--network-alias','api','--cap-drop','ALL','--security-opt','no-new-privileges','--env-file',"$private/api.env",'--mount',"type=bind,source=$private/api-persistent,target=/run/redis-persistent,readonly",'--mount',"type=bind,source=$private/api-volatile,target=/run/redis-volatile,readonly",$ApiImage) | Out-Null
    $containers += $api
    function Expect-ApiStatus([int]$Expected) {
        $probe = @'
import sys,time,urllib.request,urllib.error
expected=int(sys.argv[1])
for _ in range(60):
    try:
        with urllib.request.urlopen('http://api:8080/api/ready',timeout=3) as response:
            status=response.status
    except urllib.error.HTTPError as error:
        status=error.code
    except (urllib.error.URLError,TimeoutError):
        status=0
    if status == expected: sys.exit(0)
    time.sleep(.5)
sys.exit(1)
'@
        & docker run --rm --network $network python:3.12-slim python -c $probe $Expected *> $null
        if ($LASTEXITCODE -ne 0) {
            & docker logs $api *> (Join-Path $private 'api-readiness.log')
            throw "API readiness did not reach the expected state; private log: $private/api-readiness.log"
        }
    }
    Expect-ApiStatus 200
    $apiUid = (Invoke-TestDocker -Arguments @('exec',$api,'id','-u')).Trim()
    if ($apiUid -eq '0') { throw 'API runs as root' }
    Write-Output 'PASS non-root API starts with both role-bound secret mounts and reports ready'
    foreach ($store in @($volatile, $redis)) {
        Invoke-TestDocker -Arguments @('stop',$store) | Out-Null
        Expect-ApiStatus 503
        Invoke-TestDocker -Arguments @('start',$store) | Out-Null
        Expect-ApiStatus 200
    }
    Write-Output 'PASS API readiness refuses either Redis outage and recovers on both connections'
} finally {
    foreach ($container in $containers) { & docker rm -f $container *> $null }
    & docker network rm $network *> $null
    # Private evidence is retained under the already ignored, access-restricted directory.
}
