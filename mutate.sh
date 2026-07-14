#!/usr/bin/env bash
# #844 mutation harness. Each mutation MUST compile (a build failure means the test ran against a
# stale dll and a "survivor" would be a lie) and MUST turn the named test(s) RED.
set -u
EXE=tests/Jobbliggaren.Application.UnitTests/bin/Debug/net10.0/Jobbliggaren.Application.UnitTests.exe

run_mutation() {
  local id="$1" file="$2" desc="$3" cls="$4"
  echo ""
  echo "=============================================================="
  echo "  $id — $desc"
  echo "=============================================================="

  # 1. build MUST succeed, else the test would run against a stale dll
  if ! dotnet build tests/Jobbliggaren.Application.UnitTests/Jobbliggaren.Application.UnitTests.csproj \
        -v q --nologo 2>&1 | grep -q "Build succeeded\|^$"; then
    :
  fi
  local build
  build=$(dotnet build tests/Jobbliggaren.Application.UnitTests/Jobbliggaren.Application.UnitTests.csproj -v q --nologo 2>&1)
  if echo "$build" | grep -qE "error [A-Z]+[0-9]+"; then
    echo "  !! BUILD FAILED — mutation is INVALID (stale-dll trap). Fix the mutation."
    echo "$build" | grep -E "error" | head -3
    git checkout -- "$file"
    return 2
  fi
  echo "  build: OK (mutation compiles)"

  # 2. run the named test class
  local out
  out=$("$EXE" -class "$cls" 2>&1 | grep "Total:")
  echo "  $out"
  if echo "$out" | grep -qE "Failed: [1-9]"; then
    echo "  ==> RED (guarantee is real)"
  else
    echo "  ==> !!!!! GREEN — THE ASSERTION PROVES NOTHING !!!!!"
  fi

  # 3. restore ONLY the mutated file
  git checkout -- "$file"
}
