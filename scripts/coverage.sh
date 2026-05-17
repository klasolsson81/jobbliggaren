#!/usr/bin/env bash
# In-repo coverage-mätning (ADR 0044). Paritets-tvilling till
# scripts/coverage.ps1 (Windows-primär). Används av CI (ubuntu).
#
# Princip: rå cobertura per testprojekt lämnas OFILTRERAD (audit-trail);
# ReportGenerator producerar den filtrerade first-party-rapporten + en
# maskinläsbar summary. Filtrering sker report-time — rådatan förstörs aldrig.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts="$root/artifacts/coverage"

rm -rf "$artifacts"
mkdir -p "$artifacts"

dotnet tool restore

# Rå cobertura per testprojekt (ofiltrerad — audit-trail). Varje projekts
# TestResults/ får egen coverage.cobertura.xml; ReportGenerator globbar ihop.
export ASPNETCORE_ENVIRONMENT=Development
dotnet test --solution "$root/JobbPilot.sln" -c Release \
  -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml

cd "$root"
dotnet tool run reportgenerator \
  "-reports:tests/**/coverage.cobertura.xml" \
  "-targetdir:$artifacts" \
  "-reporttypes:Html;Cobertura;JsonSummary;TextSummary;MarkdownSummaryGithub" \
  "-assemblyfilters:+JobbPilot.Domain;+JobbPilot.Application;+JobbPilot.Infrastructure;+JobbPilot.Api;+JobbPilot.Worker;-JobbPilot.Migrate;-*.UnitTests;-*.IntegrationTests;-*.Architecture.Tests" \
  "-classfilters:-JobbPilot.Api.Migrations.*;-*.Migrations.*;-Mediator.*;-*.OpenApi.Generated.*" \
  "-filefilters:-**/Migrations/*.cs;-**/obj/**;-**/*.g.cs;-**/*.Generated.cs;-**/Program.cs"

echo ""
echo "Coverage-rapport:  $artifacts/index.html"
echo "Maskinläsbar:      $artifacts/Summary.json"
echo "PR-läsbar:         $artifacts/Summary.txt"
