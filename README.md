# mission-control

Mission Control, a thin internal control surface for Jaret and Johnny, bootstrapped from the validated ASP.NET Core 8 + React starter.

## Stack

- ASP.NET Core 8 Web API
- React 19 + Vite + TypeScript
- Root `Makefile` for common developer commands
- Root `package.json` for a single `npm run dev` entry point

## Quick start

```bash
cd /home/jaret/repos/mission-control
make setup
make dev
```

Then open:
- Frontend: `http://localhost:5173`
- API Swagger UI: `http://localhost:5181/swagger`
- API status endpoint: `http://localhost:5181/api/status`

## Useful commands

- `make setup` installs Node dependencies and restores .NET packages
- `make dev` runs the API and the Vite frontend together
- `make lint` runs the frontend linter
- `make typecheck` runs the frontend TypeScript check
- `make test` runs the .NET test suite
- `make build` builds the backend and the frontend
- `make ci` runs the local CI chain

## Repo layout

```text
backend/   ASP.NET Core API
frontend/  React + Vite app
```

## Notes

- The frontend calls `/api/status` by default.
- Vite proxies `/api` requests to `http://localhost:5181` during local development.
- .NET SDK 8 is installed locally under `~/.dotnet` on this machine.
- If a new shell does not see `dotnet`, reload your profile with `source ~/.profile`.
- MC-019 in progress as of 2026-04-22: frontend API URL construction is now centralized so configured bases with trailing slashes still resolve Create Project preview and execute routes correctly; lightweight frontend regression coverage added and validated locally.
