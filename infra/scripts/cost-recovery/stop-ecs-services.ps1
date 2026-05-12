<#
.SYNOPSIS
Stoppar JobbPilot ECS-services (api + worker) genom att sätta desired_count=0.

.DESCRIPTION
Sekundärskydd-script per F2-P4-runbook (docs/runbooks/aws-cost-recovery.md).
Triggas manuellt av incident-responder när primärskyddet (APPLY_IAM_POLICY
Budget Action på Bedrock) inte räcker eller vid misstänkt kompromiss.

AWS Budget Actions API stödjer inte custom SSM-documents för Fargate-services,
så ECS-stop måste vara manuell. Detta script är genvägen för en-knapps-respons.

.PARAMETER Cluster
ECS-cluster-namn. Default: jobbpilot-dev.

.PARAMETER Profile
AWS CLI-profil. Default: jobbpilot.

.EXAMPLE
.\stop-ecs-services.ps1
Stoppar dev-stacken med default-profil.

.EXAMPLE
.\stop-ecs-services.ps1 -Cluster jobbpilot-staging -Profile jobbpilot-staging
Stoppar staging-stacken.

.NOTES
Konsekvens: dev.jobbpilot.se returnerar 503 (ALB target-group tom).
Hangfire-jobs körs inte. Återställning via restore-ecs-services.ps1.
#>

[CmdletBinding()]
param(
    [string]$Cluster = "jobbpilot-dev",
    [string]$Profile = "jobbpilot"
)

$ErrorActionPreference = "Stop"

$services = @("jobbpilot-dev-api", "jobbpilot-dev-worker")

Write-Host "Stoppar ECS-services i cluster '$Cluster'..." -ForegroundColor Yellow
Write-Host "Profil: $Profile" -ForegroundColor Gray

foreach ($service in $services) {
    Write-Host "  -> $service (desired_count=0)" -ForegroundColor Cyan
    aws ecs update-service `
        --cluster $Cluster `
        --service $service `
        --desired-count 0 `
        --profile $Profile `
        --output text `
        --query 'service.[serviceName,desiredCount]' | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FEL: aws ecs update-service failade för $service" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Update-service skickat. Vantar 60s pa task-shutdown..." -ForegroundColor Yellow
Start-Sleep -Seconds 60

Write-Host ""
Write-Host "Status efter 60s:" -ForegroundColor Green
aws ecs describe-services `
    --cluster $Cluster `
    --services $services `
    --profile $Profile `
    --query 'services[*].[serviceName,desiredCount,runningCount,pendingCount]' `
    --output table

Write-Host ""
Write-Host "Klart. Smoke-test:" -ForegroundColor Green
Write-Host "  curl -s https://dev.jobbpilot.se/api/ready -o /dev/null -w `"%{http_code}``n`""
Write-Host "  (Forvantat: 503 nar ALB target-group ar tom)"
Write-Host ""
Write-Host "Aterstallning: .\restore-ecs-services.ps1" -ForegroundColor Yellow
