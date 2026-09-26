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
mkdir -p "$artifacts/raw"

dotnet tool restore

export ASPNETCORE_ENVIRONMENT=Development
dotnet test --solution "$root/Jobbliggaren.sln" -c Release --results-directory "$artifacts/raw" \
  -- --coverage --coverage-output-format cobertura

expected=$(grep -cE '^Project.*"tests\\.*\.csproj"' "$root/Jobbliggaren.sln")
actual=$(find "$artifacts/raw" -name '*.cobertura.xml' -type f | wc -l)
if [[ "$expected" -eq 0 || "$actual" -ne "$expected" ]]; then
  echo "::error::Expected $expected coverage reports, found $actual" >&2
  exit 1
fi

cd "$root"
dotnet tool run reportgenerator \
  "-reports:$artifacts/raw/*.cobertura.xml" \
  "-targetdir:$artifacts" \
  "-reporttypes:Html;Cobertura;JsonSummary;TextSummary;MarkdownSummaryGithub" \
  "-assemblyfilters:+Jobbliggaren.Domain;+Jobbliggaren.Application;+Jobbliggaren.Infrastructure;+Jobbliggaren.Api;+Jobbliggaren.Worker;-Jobbliggaren.Migrate;-*.UnitTests;-*.IntegrationTests;-*.Architecture.Tests" \
  "-classfilters:-Jobbliggaren.Api.Migrations.*;-*.Migrations.*;-Mediator.*;-*.OpenApi.Generated.*" \
  "-filefilters:-**/Migrations/*.cs;-**/obj/**;-**/*.g.cs;-**/*.Generated.cs;-**/Program.cs"

echo ""
echo "Coverage-rapport:  $artifacts/index.html"
echo "Maskinläsbar:      $artifacts/Summary.json"
echo "PR-läsbar:         $artifacts/Summary.txt"
