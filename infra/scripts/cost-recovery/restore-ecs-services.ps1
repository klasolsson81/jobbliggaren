<#
.SYNOPSIS
Skalar upp JobbPilot ECS-services (api + worker) till desired_count=1.

.DESCRIPTION
Återställning efter cost-recovery-incident per F2-P4-runbook
(docs/runbooks/aws-cost-recovery.md, Steg R3).

PRE-CONDITION: Grundorsaken till kostnadsblowout MÅSTE vara fixad innan
detta script körs. Att starta upp ECS igen utan att stoppa kostnads-driver
ger ny blowout inom timmar.

Scriptet detachar OCKSÅ JobbPilotBedrockDeny om policyn är attachad på
api-task-rollen (i fall Budget-cycle inte resettat ännu).

.PARAMETER Cluster
ECS-cluster-namn. Default: jobbpilot-dev.

.PARAMETER ApiDesiredCount
Antal Api-tasks att starta. Default: 1 (lean dev).

.PARAMETER WorkerDesiredCount
Antal Worker-tasks att starta. Default: 1 (lean dev).

.PARAMETER Profile
AWS CLI-profil. Default: jobbpilot.

.PARAMETER ApiTaskRoleName
Api-task-role-namn (för deny-detach). Default: jobbpilot-dev-ecs-task-api.

.PARAMETER DenyPolicyArn
JobbPilotBedrockDeny policy-ARN. Default värdet är dev-konto.

.EXAMPLE
.\restore-ecs-services.ps1
Återställer dev-stacken med defaults.

.NOTES
Vänta 3-5 min efter scriptet för ECS-deployment.
Verifiera health: curl https://dev.jobbpilot.se/api/ready
#>

[CmdletBinding()]
param(
    [string]$Cluster = "jobbpilot-dev",
    [int]$ApiDesiredCount = 1,
    [int]$WorkerDesiredCount = 1,
    [string]$Profile = "jobbpilot",
    [string]$ApiTaskRoleName = "jobbpilot-dev-ecs-task-api",
    [string]$DenyPolicyArn = "arn:aws:iam::710427215829:policy/JobbPilotBedrockDeny"
)

$ErrorActionPreference = "Stop"

Write-Host "AT ERST ALLNING: ECS-services + Bedrock-deny-detach" -ForegroundColor Yellow
Write-Host "Cluster: $Cluster" -ForegroundColor Gray
Write-Host "Profil:  $Profile" -ForegroundColor Gray
Write-Host ""

# Steg 1: Detacha JobbPilotBedrockDeny om attachad
Write-Host "Steg 1: Kontrollera JobbPilotBedrockDeny pa api-task-rollen..." -ForegroundColor Cyan
$attached = aws iam list-attached-role-policies `
    --role-name $ApiTaskRoleName `
    --profile $Profile `
    --query "AttachedPolicies[?PolicyName=='JobbPilotBedrockDeny'].PolicyArn" `
    --output text

if ([string]::IsNullOrWhiteSpace($attached)) {
    Write-Host "  -> Deny ej attachad (cycle resettad eller aldrig triggad). Hoppar over." -ForegroundColor Gray
} else {
    Write-Host "  -> Deny attachad. Detachar..." -ForegroundColor Yellow
    aws iam detach-role-policy `
        --role-name $ApiTaskRoleName `
        --policy-arn $DenyPolicyArn `
        --profile $Profile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FEL: detach-role-policy failade" -ForegroundColor Red
        exit 1
    }
    Write-Host "  -> Detach OK" -ForegroundColor Green
}

# Steg 2: Skala upp services
Write-Host ""
Write-Host "Steg 2: Skala upp ECS-services..." -ForegroundColor Cyan

$updates = @(
    @{ Name = "jobbpilot-dev-api"; Count = $ApiDesiredCount },
    @{ Name = "jobbpilot-dev-worker"; Count = $WorkerDesiredCount }
)

foreach ($u in $updates) {
    Write-Host "  -> $($u.Name) (desired_count=$($u.Count))" -ForegroundColor Cyan
    aws ecs update-service `
        --cluster $Cluster `
        --service $u.Name `
        --desired-count $u.Count `
        --profile $Profile `
        --output text `
        --query 'service.[serviceName,desiredCount]' | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FEL: update-service failade for $($u.Name)" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "ECS-deployment startad. Vantar 90s pa initial task-startup..." -ForegroundColor Yellow
Start-Sleep -Seconds 90

# Steg 3: Verifiera
Write-Host ""
Write-Host "Steg 3: Status efter 90s:" -ForegroundColor Cyan
aws ecs describe-services `
    --cluster $Cluster `
    --services jobbpilot-dev-api jobbpilot-dev-worker `
    --profile $Profile `
    --query 'services[*].[serviceName,desiredCount,runningCount,pendingCount]' `
    --output table

Write-Host ""
Write-Host "VARNING: Tasks kan ta 3-5 min for full readiness. Vanta + smoke-test:" -ForegroundColor Yellow
Write-Host "  curl -s https://dev.jobbpilot.se/api/ready -o /dev/null -w `"%{http_code}``n`""
Write-Host "  (Forvantat: 200 nar ALB target-group ar registrerad)"
Write-Host ""
Write-Host "Aterstallning klar." -ForegroundColor Green
