#!/usr/bin/env bash
# preflight-pr.sh — Single-command pre-flight PR workflow
# Usage: ./scripts/preflight-pr.sh [--skip-tests] [--skip-review] [--skip-merge] [--merge-strategy <squash|merge|rebase>] [--base <branch>]
#
# Workflow:
#   1. Create feature branch (if not already on one)
#   2. Commit uncommitted changes
#   3. Push branch to origin
#   4. Run build & unit tests locally
#   5. Create PR (or update existing)
#   6. Request Copilot code review
#   7. Wait for CI to pass
#   8. Merge PR
#
# Requires: git, gh (GitHub CLI), dotnet

set -euo pipefail

# --- Colors ---
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# --- Defaults ---
SKIP_TESTS=false
SKIP_REVIEW=false
SKIP_MERGE=false
MERGE_STRATEGY="squash"
BASE_BRANCH="main"

# --- Parse args ---
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-tests)    SKIP_TESTS=true; shift ;;
        --skip-review)   SKIP_REVIEW=true; shift ;;
        --skip-merge)    SKIP_MERGE=true; shift ;;
        --merge-strategy) MERGE_STRATEGY="$2"; shift 2 ;;
        --base)          BASE_BRANCH="$2"; shift 2 ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --skip-tests           Skip local build & test step"
            echo "  --skip-review          Skip Copilot code review"
            echo "  --skip-merge           Skip merge step (stop after CI passes)"
            echo "  --merge-strategy STR   Merge strategy: squash (default), merge, rebase"
            echo "  --base BRANCH          Base branch (default: main)"
            echo "  -h, --help             Show this help"
            exit 0 ;;
        *) echo -e "${RED}Unknown option: $1${NC}"; exit 1 ;;
    esac
done

step()   { echo -e "\n${BLUE}▶ $1${NC}"; }
ok()     { echo -e "  ${GREEN}✔ $1${NC}"; }
warn()   { echo -e "  ${YELLOW}⚠ $1${NC}"; }
fail()   { echo -e "  ${RED}✖ $1${NC}"; exit 1; }

# --- Step 1: Check for uncommitted changes ---
step "Checking working tree"
if git diff --quiet && git diff --cached --quiet; then
    ok "Working tree clean"
else
    warn "Uncommitted changes detected"
    BRANCH_NAME="preflight/$(git branch --show-current)-$(date +%Y%m%d%H%M%S)"
    echo -e "  Creating branch: ${YELLOW}${BRANCH_NAME}${NC}"
    git checkout -b "$BRANCH_NAME"
    git add -A
    git commit -m "preflight: auto-commit before PR workflow"
    ok "Changes committed to ${BRANCH_NAME}"
fi

# --- Step 2: Current branch ---
CURRENT_BRANCH=$(git branch --show-current)
step "Current branch: ${CURRENT_BRANCH}"
if [[ "$CURRENT_BRANCH" == "$BASE_BRANCH" ]]; then
    NEW_BRANCH="feature/preflight-$(date +%Y%m%d%H%M%S)"
    warn "On ${BASE_BRANCH} — creating feature branch: ${NEW_BRANCH}"
    git checkout -b "$NEW_BRANCH"
    CURRENT_BRANCH="$NEW_BRANCH"
fi

# --- Step 3: Push ---
step "Pushing to origin"
git push -u origin "$CURRENT_BRANCH" 2>/dev/null || \
    git push origin "$CURRENT_BRANCH"
ok "Pushed ${CURRENT_BRANCH}"

# --- Step 4: Local build & test ---
if [[ "$SKIP_TESTS" == "false" ]]; then
    step "Running local build & tests"
    dotnet restore --quiet 2>/dev/null
    if dotnet build --no-restore --configuration Release 2>/dev/null; then
        ok "Build succeeded"
    else
        fail "Build failed — fix errors before continuing"
    fi

    if dotnet test --no-build --configuration Release \
         --filter "FullyQualifiedName~Tests.Unit" \
         --logger "trx;LogFileName=preflight-unit.trx" \
         --results-directory ./test-results 2>/dev/null; then
        ok "Unit tests passed"
    else
        fail "Unit tests failed — fix before continuing"
    fi
else
    step "Skipping local build & tests (--skip-tests)"
fi

# --- Step 5: Create or update PR ---
step "Creating / updating PR"
PR_NUMBER=$(gh pr list --head "$CURRENT_BRANCH" --json number --jq '.[0].number' 2>/dev/null || echo "")

if [[ -n "$PR_NUMBER" ]]; then
    ok "PR #${PR_NUMBER} already exists"
else
    # Generate title from branch name
    TITLE=$(echo "$CURRENT_BRANCH" | sed 's/^feature\///' | sed 's/^preflight\///' | sed 's/-/ /g' | sed 's/\b\(.\)/\u\1/g')
    PR_NUMBER=$(gh pr create \
        --base "$BASE_BRANCH" \
        --title "$TITLE" \
        --body "Auto-generated PR from preflight workflow for branch \`$CURRENT_BRANCH\`." \
        --json number --jq '.number')
    ok "Created PR #${PR_NUMBER}"
fi

# --- Step 6: Copilot code review ---
if [[ "$SKIP_REVIEW" == "false" ]]; then
    step "Requesting Copilot code review"
    if gh copilot-review --pull-request "$PR_NUMBER" 2>/dev/null; then
        ok "Copilot review requested"
    else
        # Fallback: use the GitHub API
        gh api repos/{owner}/{repo}/pulls/"$PR_NUMBER"/reviews \
            --method POST \
            --field event=COMMENT \
            --field body="🤖 Preflight workflow: automated review request" 2>/dev/null || true
        warn "Copilot review may not be available — continuing anyway"
    fi
else
    step "Skipping Copilot review (--skip-review)"
fi

# --- Step 7: Wait for CI ---
step "Waiting for CI checks on PR #${PR_NUMBER}"
echo "  Polling every 30s for CI completion..."

MAX_WAIT=20  # 10 minutes max
ATTEMPTS=0
while [[ $ATTEMPTS -lt $MAX_WAIT ]]; do
    STATUS=$(gh pr checks "$PR_NUMBER" 2>/dev/null | tail -1 | awk '{print $NF}') || true
    CONCLUSION=$(gh pr checks "$PR_NUMBER" --json name,state --jq '.[0].state' 2>/dev/null || echo "unknown")

    if gh pr checks "$PR_NUMBER" 2>/dev/null | grep -qi "fail\|cancel"; then
        fail "CI checks failed — review and fix before merging"
    fi

    if gh pr checks "$PR_NUMBER" 2>/dev/null | grep -qi "pass"; then
        ok "CI checks passed"
        break
    fi

    ATTEMPTS=$((ATTEMPTS + 1))
    echo "  ...waiting (attempt $ATTEMPTS/$MAX_WAIT)"
    sleep 30
done

if [[ $ATTEMPTS -ge $MAX_WAIT ]]; then
    warn "Timed out waiting for CI — check manually with: gh pr checks $PR_NUMBER"
fi

# --- Step 8: Merge ---
if [[ "$SKIP_MERGE" == "false" ]]; then
    step "Merging PR #${PR_NUMBER}"
    gh pr merge "$PR_NUMBER" --"$MERGE_STRATEGY" --delete-branch
    ok "PR #${PR_NUMBER} merged (${MERGE_STRATEGY})"

    # Pull the merged changes
    git checkout "$BASE_BRANCH"
    git pull origin "$BASE_BRANCH"
    ok "Pulled ${BASE_BRANCH}"
else
    step "Skipping merge (--skip-merge)"
    echo -e "  PR #${PR_NUMBER} is ready for manual merge: gh pr merge ${PR_NUMBER} --${MERGE_STRATEGY}"
fi

echo -e "\n${GREEN}✅ Preflight PR workflow complete!${NC}"