#!/usr/bin/env bash
set -euo pipefail

required_files=(
  "README.md"
  ".gitignore"
  ".editorconfig"
  ".env.example"
  "LICENSE"
  ".github/workflows/ci.yml"
  "Makefile"
  ".markdownlint.json"
  ".prettierrc.json"
  "scripts/check-env.sh"
  "scripts/opencode-harness.sh"
)

for path in "${required_files[@]}"; do
  if [[ ! -f "$path" ]]; then
    echo "FAIL: missing required baseline file: $path"
    exit 1
  fi
done

if ! grep -q '/home/jaret/repos' scripts/opencode-harness.sh; then
  echo "FAIL: harness script does not default to /home/jaret/repos"
  exit 1
fi

if ! grep -q '^PROJECT_NAME=' .env.example; then
  echo "FAIL: .env.example is missing PROJECT_NAME"
  exit 1
fi

if ! grep -q '^NODE_ENV=' .env.example; then
  echo "FAIL: .env.example is missing NODE_ENV"
  exit 1
fi

echo "PASS: template baseline smoke checks succeeded"
