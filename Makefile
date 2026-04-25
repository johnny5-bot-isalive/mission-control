SHELL := /usr/bin/env bash
DOTNET ?= $(if $(shell command -v dotnet 2>/dev/null),$(shell command -v dotnet),$(HOME)/.dotnet/dotnet)
NPM ?= npm

.PHONY: setup env-check format format-check lint test typecheck build dev ci

setup:
	@$(NPM) install
	@$(NPM) --prefix frontend install
	@$(DOTNET) restore MissionControl.sln

env-check:
	@bash scripts/check-env.sh
	@$(DOTNET) --version >/dev/null
	@node --version >/dev/null
	@$(NPM) --version >/dev/null
	@echo "Environment looks good."

format:
	@if command -v $(DOTNET) >/dev/null 2>&1 || [ -x "$(DOTNET)" ]; then \
	  $(DOTNET) format MissionControl.sln; \
	fi
	@if command -v prettier >/dev/null 2>&1; then \
	  prettier --write README.md frontend/src/**/*.{ts,tsx,css} frontend/*.json frontend/*.ts frontend/index.html .github/workflows/*.yml; \
	else \
	  echo "prettier not installed globally, skipping JS/TS/Markdown formatting"; \
	fi

format-check:
	@$(DOTNET) format MissionControl.sln --verify-no-changes --verbosity minimal
	@if command -v prettier >/dev/null 2>&1; then \
	  prettier --check README.md frontend/src/**/*.{ts,tsx,css} frontend/*.json frontend/*.ts frontend/index.html .github/workflows/*.yml; \
	else \
	  echo "prettier not installed globally, skipping JS/TS/Markdown format check"; \
	fi

lint:
	@$(NPM) --prefix frontend run lint

test:
	@$(DOTNET) test MissionControl.sln
	@$(NPM) --prefix frontend run test

typecheck:
	@$(NPM) --prefix frontend run typecheck

build:
	@$(DOTNET) build MissionControl.sln --no-restore
	@$(NPM) --prefix frontend run build

dev:
	@$(NPM) run dev

ci: env-check lint typecheck test build
