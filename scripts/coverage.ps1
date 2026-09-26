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
$raw = Join-Path $artifacts 'raw'
New-Item -ItemType Directory -Force -Path $raw | Out-Null

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed: $LASTEXITCODE" }

# ASPNETCORE_ENVIRONMENT=Development speglar CI (ForwardedHeaders-fail-loud).
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet test --solution (Join-Path $root 'Jobbliggaren.sln') -c Release --results-directory $raw `
  -- --coverage --coverage-output-format cobertura
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $LASTEXITCODE" }

$expected = @(Select-String -Path (Join-Path $root 'Jobbliggaren.sln') -Pattern '^Project.*"tests\\.*\.csproj"').Count
$actual = @(Get-ChildItem -LiteralPath $raw -Filter '*.cobertura.xml' -File).Count
if ($expected -eq 0 -or $actual -ne $expected) {
  throw "Expected $expected coverage reports, found $actual"
}

Set-Location $root
dotnet tool run reportgenerator `
  "-reports:$raw/*.cobertura.xml" `
  "-targetdir:$artifacts" `
  "-reporttypes:Html;Cobertura;JsonSummary;TextSummary;MarkdownSummaryGithub" `
  "-assemblyfilters:+Jobbliggaren.Domain;+Jobbliggaren.Application;+Jobbliggaren.Infrastructure;+Jobbliggaren.Api;+Jobbliggaren.Worker;-Jobbliggaren.Migrate;-*.UnitTests;-*.IntegrationTests;-*.Architecture.Tests" `
  "-classfilters:-Jobbliggaren.Api.Migrations.*;-*.Migrations.*;-Mediator.*;-*.OpenApi.Generated.*" `
  "-filefilters:-**/Migrations/*.cs;-**/obj/**;-**/*.g.cs;-**/*.Generated.cs;-**/Program.cs"

if ($LASTEXITCODE -ne 0) { throw "ReportGenerator failed: $LASTEXITCODE" }

Write-Host ""
Write-Host "Coverage-rapport:  $artifacts/index.html"
Write-Host "Maskinläsbar:      $artifacts/Summary.json"
Write-Host "PR-läsbar:         $artifacts/Summary.txt"
