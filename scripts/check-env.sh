#!/usr/bin/env bash
# check-env.sh — validate .env.example coverage against an optional local .env
set -euo pipefail

env_example=".env.example"
env_file=".env"

if [[ ! -f "$env_example" ]]; then
  echo "SKIP: $env_example not found"
  exit 0
fi

required_vars=$(awk -F= '
  /^[[:space:]]*#/ { next }
  /^[[:space:]]*$/ { next }
  { print $1 }
' "$env_example" | sort -u)

echo "Required vars from $env_example:"
if [[ -n "$required_vars" ]]; then
  echo "$required_vars"
else
  echo "(none declared)"
fi

if [[ -f "$env_file" ]]; then
  actual_vars=$(awk -F= '
    /^[[:space:]]*#/ { next }
    /^[[:space:]]*$/ { next }
    { print $1 }
  ' "$env_file" | sort -u)

  echo ""
  echo "Actual vars in $env_file:"
  if [[ -n "$actual_vars" ]]; then
    echo "$actual_vars"
  else
    echo "(none set)"
  fi

  if [[ -n "$required_vars" ]]; then
    missing=$(comm -23 <(echo "$required_vars") <(echo "$actual_vars"))
    if [[ -n "$missing" ]]; then
      echo ""
      echo "WARNING: missing vars not yet set:"
      echo "$missing"
    else
      echo ""
      echo "OK: all required vars are present"
    fi
  else
    echo ""
    echo "OK: no required vars are declared in $env_example"
  fi
else
  echo ""
  if [[ -n "$required_vars" ]]; then
    echo "INFO: $env_file not present. Copy $env_example to $env_file and fill in values for local work."
  else
    echo "OK: no required vars are declared and $env_file is not needed yet"
  fi
fi
