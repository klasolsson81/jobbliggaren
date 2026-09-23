param([string]$WorkerImage = 'jbl-1759-worker:pr2')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = 'jbl-1759-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$network = $runId + '-network'
$redis = $runId + '-redis'
$postgres = $runId + '-postgres'
$worker = $runId + '-worker'
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
    $connection = [IO.File]::ReadAllText((Join-Path $private 'worker-persistent/connection')).Replace('localhost:6379', 'redis:6379')
    [IO.File]::WriteAllText((Join-Path $private 'worker-persistent/connection'), $connection)
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
} finally {
    foreach ($container in $containers) { & docker rm -f $container *> $null }
    & docker network rm $network *> $null
    # Private evidence is retained under the already ignored, access-restricted directory.
}
