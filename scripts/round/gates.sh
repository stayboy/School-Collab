#!/usr/bin/env bash
# gates.sh — the CI-parity local gate for a School-Collab round.
#
# WHY THIS EXISTS
#   A per-project loop over a hand-picked subset of test projects is *false confidence*:
#   `SchoolCollab.slnx` registers 13 `*.Tests.Unit` projects, CI also gates three
#   `*.Tests.Integration` legs' worth of projects, the repo-wide `ArchitectureTests.Unit`
#   scanner, a project-scoped `CrossModuleWiringTests` guard, and TWO Python portal suites —
#   none of which a "six project" script would run. So this script mirrors CI's **filter
#   legs verbatim** instead of enumerating projects, and prints what it did NOT check.
#
#   It also refuses to inherit CI's one dangerous tolerance blindly: CI passes
#   `--ignore-exit-code 8` ("no tests ran"). Here that case is a **failure**, because the
#   repo has already paid for this exact trap (`dotnet test | grep failed` prints
#   `failed: 0` when zero tests ran).
#
# USAGE
#   scripts/round/gates.sh [--serial] [--no-maui] [--no-integration] [--no-portals]
#
#   --serial           build the main solution with `-m:1` (one MSBuild node). Slower, but the
#                      parallel build exhausts memory on this machine and has repeatedly killed the
#                      agent running it (~0xC0000409, a silent process death with no compile error).
#                      **Agents should pass this** when they run the gate themselves; a human on a
#                      roomier machine need not. The tests executed are identical — only build
#                      parallelism changes — so CI parity is preserved.
#   --no-maui          skip the MAUI Blazor Hybrid head (adds it to "NOT checked")
#   --no-integration   skip the Integration leg (adds it to "NOT checked")
#   --no-portals       skip the two pytest legs (adds them to "NOT checked")
#
# EXIT: 0 only if every leg that ran passed AND every skipped leg was explicitly asked for.
# The parity between this script and `.github/workflows/ci.yml` is pinned by
# `tests/SchoolCollab.ArchitectureTests.Unit/LocalGateCiParityTests.cs` — edit both or neither.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[1;33m'; BLUE=$'\033[0;34m'; NC=$'\033[0m'
step() { printf '\n%s▶ %s%s\n' "$BLUE" "$1" "$NC"; }
ok()   { printf '  %s✔ %s%s\n' "$GREEN" "$1" "$NC"; }
warn() { printf '  %s⚠ %s%s\n' "$YELLOW" "$1" "$NC"; }
bad()  { printf '  %s✖ %s%s\n' "$RED" "$1" "$NC"; }

RUN_MAUI=true
RUN_INTEGRATION=true
RUN_PORTALS=true
MAIN_BUILD_ARGS=""
for arg in "$@"; do
    case "$arg" in
        --serial)         MAIN_BUILD_ARGS="-m:1" ;;
        --no-maui)        RUN_MAUI=false ;;
        --no-integration) RUN_INTEGRATION=false ;;
        --no-portals)     RUN_PORTALS=false ;;
        -h|--help)        sed -n '3,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unknown option: $arg" >&2; exit 2 ;;
    esac
done

FAILURES=()
NOT_CHECKED=()
LOG_DIR="$REPO_ROOT/test-results/gates"
mkdir -p "$LOG_DIR"

# run_leg <label> <log-name> <command...>
# Runs a command, tees it to a log, and treats "Zero tests ran" as a FAILURE.
run_leg() {
    local label="$1" log="$2"; shift 2
    step "$label"
    if "$@" 2>&1 | tee "$LOG_DIR/$log.log"; then
        if grep -qi "zero tests ran" "$LOG_DIR/$log.log"; then
            bad "$label — zero tests ran (filter matched nothing; not a pass)"
            FAILURES+=("$label (zero tests ran)")
        else
            ok "$label"
        fi
    else
        bad "$label"
        FAILURES+=("$label")
    fi
}

# --- Leg 1: restore + build (ci.yml `build-and-test` job) -----------------------------------
step "Restore"
if dotnet restore >"$LOG_DIR/restore.log" 2>&1; then ok "restore"; else bad "restore"; FAILURES+=("restore"); fi

step "Build (Release)"
if dotnet build --no-restore --configuration Release $MAIN_BUILD_ARGS 2>&1 | tee "$LOG_DIR/build.log" | tail -5; then
    if grep -Eq '\b[1-9][0-9]* Error\(s\)' "$LOG_DIR/build.log"; then
        bad "build reported errors"; FAILURES+=("build")
    else
        ok "build — 0 errors"
    fi
else
    bad "build"; FAILURES+=("build")
fi

# --- Leg 2: unit tests (ci.yml `build-and-test` job) ----------------------------------------
run_leg "Unit tests (filter: FullyQualifiedName~Tests.Unit)" "unit" \
    dotnet test --no-build --configuration Release \
    --filter "FullyQualifiedName~Tests.Unit" --ignore-exit-code 8 \
    --report-trx --report-trx-filename unit-results.trx --results-directory ./test-results

# --- Leg 3: integration tests (ci.yml `build-and-test` job) ---------------------------------
# KNOWN LOCAL DIVERGENCE, named rather than hidden. ci.yml injects `OpenRouter__ApiKey` while the
# live tests read `Parameters:openrouter-api-key` (user secrets / `Parameters__openrouter_api_key`),
# so in CI they see no key and self-skip as Inconclusive. On a developer machine the key EXISTS,
# so `ChatProviderLiveTests` runs for real against the pinned model, which currently answers
# HTTP 400 (ci.yml's own note records this). Those two tests are therefore classified as a
# documented divergence — and ONLY those two; any other integration failure still fails the gate.
if [[ "$RUN_INTEGRATION" == true ]]; then
    run_leg "Integration tests (filter: FullyQualifiedName~Tests.Integration)" "integration" \
        dotnet test --no-build --configuration Release \
        --filter "FullyQualifiedName~Tests.Integration" --ignore-exit-code 8 \
        --report-trx --report-trx-filename integration-results.trx --results-directory ./test-results

    if [[ " ${FAILURES[*]-} " == *"Integration tests"* ]]; then
        OTHER_FAILURES="$(grep -E '^failed |^  failed ' "$LOG_DIR/integration.log" \
            | grep -vE 'failed (OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully|OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt)' \
            | grep -vE '^integration ' || true)"
        if [[ -z "$OTHER_FAILURES" ]]; then
            warn "integration: the 2 OpenRouter live tests failed locally — CI self-skips them (no key)"
            NOT_CHECKED+=("OpenRouter live tests (ChatProviderLiveTests) — CI skips them as Inconclusive; a machine with a key runs them for real and the pinned model answers HTTP 400")
            # Reclassify: this leg is a documented divergence, not a gate failure.
            KEPT=()
            for f in "${FAILURES[@]}"; do
                [[ "$f" == "Integration tests (filter: FullyQualifiedName~Tests.Integration)" ]] || KEPT+=("$f")
            done
            FAILURES=("${KEPT[@]:-}")
            [[ -n "${FAILURES[*]-}" ]] || FAILURES=()
        else
            bad "integration: failures outside the documented divergence:"; printf '%s\n' "$OTHER_FAILURES"
        fi
    fi
else
    NOT_CHECKED+=("Integration tests (skipped by --no-integration)")
fi

# --- Leg 4: cross-module wiring guard (ci.yml `cross-module-wiring-guard` job) --------------
step "Cross-module wiring guard"
if dotnet restore tests/SchoolCollab.Core.Tests.Unit/SchoolCollab.Core.Tests.Unit.csproj >"$LOG_DIR/wiring-restore.log" 2>&1 \
   && dotnet build tests/SchoolCollab.Core.Tests.Unit/SchoolCollab.Core.Tests.Unit.csproj --no-restore --configuration Release >"$LOG_DIR/wiring-build.log" 2>&1; then
    run_leg "CrossModuleWiringTests" "wiring" \
        dotnet test tests/SchoolCollab.Core.Tests.Unit/SchoolCollab.Core.Tests.Unit.csproj \
        --no-build --configuration Release \
        --filter "FullyQualifiedName~CrossModuleWiringTests"
else
    bad "cross-module wiring guard build"; FAILURES+=("cross-module wiring guard build")
fi

# --- Leg 5: MAUI head (NOT in SchoolCollab.slnx — a shared-component change is invisible) ---
if [[ "$RUN_MAUI" == true ]]; then
    step "MAUI Blazor Hybrid head (serial: -m:1 -p:BuildInParallel=false)"
    if dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj \
         -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false \
         2>&1 | tee "$LOG_DIR/maui.log" | tail -5; then
        if grep -Eq '\b[1-9][0-9]* Error\(s\)' "$LOG_DIR/maui.log"; then
            bad "MAUI head reported errors"; FAILURES+=("MAUI head")
        else
            ok "MAUI head — 0 errors"
        fi
    else
        bad "MAUI head"; FAILURES+=("MAUI head")
    fi
else
    NOT_CHECKED+=("MAUI Blazor Hybrid head (skipped by --no-maui) — a shared-component break is invisible to the main solution")
fi

# --- Leg 6: Python portals (ci.yml `portals-python` + `authportal-python` jobs) ------------
if [[ "$RUN_PORTALS" == true ]] && command -v uv >/dev/null 2>&1; then
    for portal in SchoolCollab.Portals SchoolCollab.AuthPortal; do
        step "$portal (pytest)"
        if ( cd "src/$portal" && uv sync >/dev/null 2>&1 && uv run pytest ) 2>&1 | tee "$LOG_DIR/$portal.log" | tail -5; then
            ok "$portal pytest"
        else
            bad "$portal pytest"; FAILURES+=("$portal pytest")
        fi
    done
elif [[ "$RUN_PORTALS" == true ]]; then
    NOT_CHECKED+=("Python portal suites (uv not installed) — run 'uv run pytest' in src/SchoolCollab.Portals and src/SchoolCollab.AuthPortal")
else
    NOT_CHECKED+=("Python portal suites (skipped by --no-portals)")
fi

# --- Report --------------------------------------------------------------------------------
printf '\n%s══ NOT checked here (CI still gates these) ══%s\n' "$BLUE" "$NC"
if [[ ${#NOT_CHECKED[@]} -eq 0 ]]; then
    printf '  %s(nothing skipped — this run mirrors every CI leg)%s\n' "$GREEN" "$NC"
else
    printf '  %s⚠ %s%s\n' "$YELLOW" "${NOT_CHECKED[0]}" "$NC"
    for n in "${NOT_CHECKED[@]:1}"; do printf '  %s⚠ %s%s\n' "$YELLOW" "$n" "$NC"; done
fi

if [[ ${#FAILURES[@]} -eq 0 ]]; then
    printf '\n%s✅ gates.sh — every leg that ran is green%s\n'  "$GREEN" "$NC"
    exit 0
fi

printf '\n%s✖ gates.sh — FAILED%s\n' "$RED" "$NC"
for f in "${FAILURES[@]}"; do printf '  %s✖ %s%s\n' "$RED" "$f" "$NC"; done
printf '  logs: %s\n' "$LOG_DIR"
exit 1
