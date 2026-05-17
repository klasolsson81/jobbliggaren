#!/usr/bin/env pwsh
# In-repo coverage-mätning (ADR 0044). Reproducerbar både lokalt (Windows-
# primär) och i CI (ubuntu, scripts/coverage.sh är paritets-tvilling).
#
# Princip: rå cobertura per testprojekt lämnas OFILTRERAD (audit-trail);
# ReportGenerator producerar den filtrerade first-party-rapporten + en
# maskinläsbar summary. Filtrering sker report-time — rådatan förstörs aldrig.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts/coverage'

Remove-Item -Recurse -Force $artifacts -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

dotnet tool restore

# Rå cobertura per testprojekt (ofiltrerad — audit-trail). Varje projekts
# TestResults/ får egen coverage.cobertura.xml; ReportGenerator globbar ihop.
# ASPNETCORE_ENVIRONMENT=Development speglar CI (ForwardedHeaders-fail-loud).
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet test --solution (Join-Path $root 'JobbPilot.sln') -c Release `
  -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml

Set-Location $root
dotnet tool run reportgenerator `
  "-reports:tests/**/coverage.cobertura.xml" `
  "-targetdir:$artifacts" `
  "-reporttypes:Html;Cobertura;JsonSummary;TextSummary;MarkdownSummaryGithub" `
  "-assemblyfilters:+JobbPilot.Domain;+JobbPilot.Application;+JobbPilot.Infrastructure;+JobbPilot.Api;+JobbPilot.Worker;-JobbPilot.Migrate;-*.UnitTests;-*.IntegrationTests;-*.Architecture.Tests" `
  "-classfilters:-JobbPilot.Api.Migrations.*;-*.Migrations.*;-Mediator.*;-*.OpenApi.Generated.*" `
  "-filefilters:-**/Migrations/*.cs;-**/obj/**;-**/*.g.cs;-**/*.Generated.cs;-**/Program.cs"

Write-Host ""
Write-Host "Coverage-rapport:  $artifacts/index.html"
Write-Host "Maskinläsbar:      $artifacts/Summary.json"
Write-Host "PR-läsbar:         $artifacts/Summary.txt"
