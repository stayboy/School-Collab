#!/usr/bin/env bash
# wait-ci.sh — ONE call, ONE line, never a sleep loop.
#
# WHY
#   A `while ...; do sleep 60; gh pr checks; done` loop bills a line of context per
#   iteration, cannot be interrupted without losing everything it waited for (a 5-minute
#   abort returns NOTHING), and drifts when the next poll cadence is guessed. GitHub's
#   `--watch` mode already blocks until the checks settle; the only things it lacks are a
#   hard timeout and a bounded verdict, which is what this wrapper adds.
#
# USAGE
#   scripts/round/wait-ci.sh --pr <number> [--timeout <seconds>]
#   scripts/round/wait-ci.sh --run <run-id> [--timeout <seconds>]
#
#   --timeout   hard ceiling, default 1800 (30 min). On expiry the command is killed and
#               the exit code is 3, so a caller can distinguish "still running" from "red".
#
# EXIT
#   0  every check passed (skipped/path-filtered checks do not count as failures)
#   1  at least one check failed   — the failing names are printed
#   3  timed out before the checks settled
#   2  usage error
#
# CONTEXT BUDGET: at most three lines of output, regardless of how long CI takes.
# This is the parent's tool. A worker never waits on CI — it runs build + affected tests.

set -uo pipefail

TARGET_KIND=""
TARGET=""
TIMEOUT=1800
while [[ $# -gt 0 ]]; do
    case "$1" in
        --pr)      TARGET_KIND=pr;  TARGET="$2"; shift 2 ;;
        --run)     TARGET_KIND=run; TARGET="$2"; shift 2 ;;
        --timeout) TIMEOUT="$2"; shift 2 ;;
        -h|--help) sed -n '3,26p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$TARGET_KIND" && -n "$TARGET" ]] || { echo "usage: wait-ci.sh --pr <n> | --run <id> [--timeout s]" >&2; exit 2; }

LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT

if [[ "$TARGET_KIND" == "pr" ]]; then
    timeout "$TIMEOUT" gh pr checks "$TARGET" --watch --interval 60 >"$LOG" 2>&1
    rc=$?
    LABEL="PR #$TARGET"
else
    timeout "$TIMEOUT" gh run watch "$TARGET" --exit-status --interval 30 >"$LOG" 2>&1
    rc=$?
    LABEL="run $TARGET"
fi

# timeout(1) exits 124 when it kills the child.
if [[ $rc -eq 124 ]]; then
    printf '%s: TIMEOUT after %ss — still not settled; re-run this call to keep waiting\n' "$LABEL" "$TIMEOUT"
    exit 3
fi

pass=$(grep -c $'\tpass\t'     "$LOG" || true)
fail=$(grep -c $'\tfail\t'     "$LOG" || true)
skip=$(grep -c $'\tskipping\t' "$LOG" || true)
pend=$(grep -c $'\tpending\t'  "$LOG" || true)

if [[ "$fail" -gt 0 ]]; then
    printf '%s: FAILED — %s pass, %s fail, %s skipped\n' "$LABEL" "$pass" "$fail" "$skip"
    grep $'\tfail\t' "$LOG" | awk -F'\t' '{printf "  ✖ %s\n", $1}' | head -10
    exit 1
fi

# A PR with no checks at all is NOT green — the same trap as `dotnet test | grep failed`
# printing `failed: 0` for "Zero tests ran". Verified 2026-10-20: a bogus PR id reported
# "GREEN — 0 pass" before this guard existed.
if [[ "$pass" -eq 0 && "$skip" -eq 0 ]]; then
    printf '%s: NO CHECKS FOUND — refusing to report green\n' "$LABEL"
    head -5 "$LOG" | sed 's/^/  /'
    exit 1
fi

if [[ "$pend" -gt 0 ]]; then
    printf '%s: %s pass, %s pending, %s skipped — not settled (no failures yet)\n' "$LABEL" "$pass" "$pend" "$skip"
    exit 3
fi

printf '%s: GREEN — %s pass, %s skipped (path-filtered)\n' "$LABEL" "$pass" "$skip"
exit 0
