#!/usr/bin/env bash
# deliver.sh — the mechanical half of a round hand-over: stage an explicitly-named path set,
# prove nothing else was staged, show the change set, commit, STOP.
#
# WHY THIS EXISTS
#   A round's git leg is ~30 lines of brief repeated every time, and the brief is where the
#   owner's gates live. This script carries only the mechanical part. It deliberately does
#   **not** push, open a PR, or merge:
#
#     * push  — gated by the owner's word and by `.githooks/pre-push` (SCHOOLCOLLAB_ALLOW_PUSH=1)
#     * PR    — the body is authored judgement (Summary / Evidence / Merge Danger), not mechanics
#     * merge — a script cannot know whether the owner authorized it
#
#   What it does enforce, because these are the two ways round commits have gone wrong here:
#     1. **Path allowlist** — nothing outside the named path set may be staged. Policy-layer
#        files (`.pi/skills/**`, `AGENTS.md`, `documents/specs/**`) never ride a round commit
#        (AGENTS.md L3), and a stray `git add -A` once pulled them in.
#     2. **Named set is explicit** — no globs, no `-A`. Every path is expanded and shown.
#
# USAGE
#   scripts/round/deliver.sh --message <file> [--check] -- <path> [<path> ...]
#
#   --message <file>  commit message file (subject on line 1)
#   --check           stage + verify + print, then STOP before committing
#
# EXIT: 0 staged/committed cleanly; 2 usage error; 3 forbidden path; 4 nothing to commit.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[1;33m'; BLUE=$'\033[0;34m'; NC=$'\033[0m'
step() { printf '\n%s▶ %s%s\n' "$BLUE" "$1" "$NC"; }
ok()   { printf '  %s✔ %s%s\n' "$GREEN" "$1" "$NC"; }
bad()  { printf '  %s✖ %s%s\n' "$RED" "$1" "$NC"; }

MESSAGE_FILE=""
CHECK_ONLY=false
PATHS=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --message) MESSAGE_FILE="$2"; shift 2 ;;
        --check)   CHECK_ONLY=true; shift ;;
        --)        shift; PATHS=("$@"); break ;;
        -h|--help) sed -n '3,35p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

[[ -n "$MESSAGE_FILE" && -f "$MESSAGE_FILE" ]] || { echo "--message <file> is required and must exist" >&2; exit 2; }
[[ ${#PATHS[@]} -gt 0 ]] || { echo "No paths given after --" >&2; exit 2; }

# --- Gate 1: never commit on the delivery branch -------------------------------------------
BRANCH="$(git branch --show-current)"
step "Branch"
if [[ "$BRANCH" == "main" ]]; then
    bad "on 'main' — round commits go to a feature/fix branch (AGENTS.md merge policy)"
    exit 3
fi
ok "$BRANCH"

# --- Gate 2: expand the named set, then prove the staged set is a subset --------------------
step "Expanding the named path set"
EXPECTED="$(mktemp)"; STAGED="$(mktemp)"
trap 'rm -f "$EXPECTED" "$STAGED"' EXIT

for p in "${PATHS[@]}"; do
    if [[ -d "$p" ]]; then
        git ls-files -- "$p" >>"$EXPECTED"
        git ls-files --others --exclude-standard -- "$p" >>"$EXPECTED"
    elif [[ -f "$p" ]]; then
        printf '%s\n' "$p" >>"$EXPECTED"
    else
        bad "no such path: $p"; exit 2
    fi
done
sort -u -o "$EXPECTED" "$EXPECTED"
ok "$(wc -l <"$EXPECTED" | tr -d ' ') path(s) named"

git add -A -- "${PATHS[@]}"
git diff --cached --name-only | sort -u >"$STAGED"

step "Staged-vs-named diff (must be empty)"
EXTRA="$(comm -13 "$EXPECTED" "$STAGED")"
if [[ -n "$EXTRA" ]]; then
    bad "staged but NOT in the named set:"
    printf '%s\n' "$EXTRA"
    exit 3
fi
ok "nothing staged outside the named set"

step "Policy-layer exclusion (AGENTS.md L3)"
FORBIDDEN="$(grep -E '^(\.pi/skills/|AGENTS\.md$|documents/specs/|\.githooks/|\.github/copilot/)' "$STAGED" || true)"
if [[ -n "$FORBIDDEN" ]]; then
    bad "policy-layer paths are staged — remove them (their own PR carries them):"
    printf '%s\n' "$FORBIDDEN"
    exit 3
fi
ok "no policy-layer paths"

# --- Show the change set -------------------------------------------------------------------
step "Change set"
git diff --cached --stat | tail -20
printf '  %s files\n' "$(wc -l <"$STAGED" | tr -d ' ')"

if git diff --cached --quiet; then
    bad "nothing to commit"; exit 4
fi

if [[ "$CHECK_ONLY" == true ]]; then
    step "STOP (--check)"
    printf '  staged, verified, not committed\n'
    exit 0
fi

# --- Commit, then stop ---------------------------------------------------------------------
step "Commit"
git commit -F "$MESSAGE_FILE"
ok "committed $(git rev-parse --short HEAD)"

step "STOP — deliberately not done here"
printf '  push    → owner word + SCHOOLCOLLAB_ALLOW_PUSH=1 git push -u origin %s\n' "$BRANCH"
printf '  PR      → authored body, then gh pr create / gh stack submit\n'
printf '  merge   → owner word only\n'
exit 0
