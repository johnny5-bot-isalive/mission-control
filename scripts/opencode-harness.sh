#!/usr/bin/env bash
# opencode-harness.sh — scaffold a new OpenCode-managed repo in the repo home
set -euo pipefail

REPO_HOME="${OPENCODE_REPO_HOME:-/home/jaret/repos}"
TEMPLATE_REPO="opencode-test-repo"

usage() {
  echo "Usage: $0 <new-repo-name>"
  echo "  Creates a new repo by copying '$TEMPLATE_REPO' into $REPO_HOME"
  exit 1
}

if [[ $# -lt 1 ]]; then
  usage
fi

new_name="$1"
new_path="$REPO_HOME/$new_name"

if [[ -d "$new_path" ]]; then
  echo "ERROR: $new_path already exists"
  exit 1
fi

cp -r "$REPO_HOME/$TEMPLATE_REPO" "$new_path"
cd "$new_path"

# Reset git and strip local-only files copied from the template workspace
rm -rf .git
rm -f .env

git init
git add -A
git commit -m "Initial commit from $TEMPLATE_REPO template"

echo ""
echo "Created: $new_path"
echo "Next: push to GitHub and configure repo settings"
