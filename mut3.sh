#!/usr/bin/env bash
set -u
EXE=./tests/Jobbliggaren.Application.UnitTests/bin/Debug/net10.0/Jobbliggaren.Application.UnitTests.exe
CLS="Jobbliggaren.Application.UnitTests.Resumes.Parsing.PreambleResidueTests"

run_mutation() {
  local id="$1" file="$2" desc="$3"
  echo ""
  echo "== $id — $desc"
  local build
  build=$(dotnet build tests/Jobbliggaren.Application.UnitTests/Jobbliggaren.Application.UnitTests.csproj -v q --nologo 2>&1)
  if echo "$build" | grep -qE "error [A-Z]+[0-9]+"; then
    echo "   !! BUILD FAILED — mutation INVALID (stale-dll trap), NOT a survivor"
    echo "$build" | grep -E "error" | head -2
    git checkout -- "$file"
    dotnet build tests/Jobbliggaren.Application.UnitTests/Jobbliggaren.Application.UnitTests.csproj -v q --nologo >/dev/null 2>&1
    return 2
  fi
  echo "   build: OK (compiles)"
  local out
  out=$("$EXE" -class "$CLS" 2>&1 | grep "Total:")
  echo "   $out"
  if echo "$out" | grep -qE "Failed: [1-9]"; then echo "   ==> RED"; else echo "   ==> !!! GREEN — PROVES NOTHING !!!"; fi
  git checkout -- "$file"
  dotnet build tests/Jobbliggaren.Application.UnitTests/Jobbliggaren.Application.UnitTests.csproj -v q --nologo >/dev/null 2>&1
}
