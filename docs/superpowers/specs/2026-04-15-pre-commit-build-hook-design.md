# Pre-Commit Build Hook Design

**Date:** 2026-04-15
**Status:** Approved
**Approach:** Husky (Git hooks manager for Node.js)

## Goal

Ensure both backend (.NET) and frontend (React/TypeScript) build and pass tests **before** any commit reaches the repository. This mirrors the CI pipeline locally, catching failures early.

## Concept: How Git Hooks Work

Git runs scripts from `.git/hooks/` at specific lifecycle events. A `pre-commit` hook runs before every commit:

- **Exit 0** (all commands succeed) -> commit proceeds
- **Exit non-zero** (any command fails) -> commit is blocked

The problem: `.git/hooks/` is not tracked by Git, so hooks can't be shared via the repo. **Husky** solves this by storing hooks in `.husky/` (tracked) and registering that directory with Git via a `prepare` script.

## What Gets Added

```
menunest/
├── .husky/
│   └── pre-commit            # Shell script — the actual hook
├── frontend/
│   └── package.json          # +husky devDep, +prepare script
└── backend/
```

### package.json Changes

```json
{
  "scripts": {
    "prepare": "cd .. && husky frontend/.husky"
  },
  "devDependencies": {
    "husky": "^9.x"
  }
}
```

The `prepare` script runs automatically after `npm install`, so any developer who clones and installs gets the hooks with zero extra setup.

### Pre-Commit Script

```bash
#!/bin/sh
set -e   # Exit immediately if any command fails

# --- Backend: build + test ---
cd backend
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release --verbosity minimal

# --- Frontend: typecheck + build ---
cd ../frontend
npx tsc --noEmit
npm run build
```

Commands run sequentially. If any fails, the script exits non-zero and the commit is blocked.

## Checks Performed

| Step | Command | Time Est. | What It Catches |
|------|---------|-----------|-----------------|
| Backend build | `dotnet build --no-restore --configuration Release` | 8-15s | Compilation errors in C# |
| Backend test | `dotnet test --no-build --configuration Release` | 5-10s | Broken unit tests |
| Frontend typecheck | `npx tsc --noEmit` | 3-5s | TypeScript type errors |
| Frontend build | `npm run build` | 5-10s | Vite build failures, missing imports |

**Total estimated time:** ~20-40 seconds per commit.

## Edge Cases

| Situation | Behavior |
|-----------|----------|
| `dotnet` not installed | Script fails, commit blocked, error message shown |
| `node_modules` not installed | `npx tsc` fails, commit blocked |
| Developer needs to skip | `git commit --no-verify` bypasses the hook |
| Backend unchanged | Still runs -- simple and safe |

## Escape Hatch

`git commit --no-verify` skips the pre-commit hook. This is acceptable for WIP commits because the GitHub Actions CI pipeline still runs on push and catches anything that slips through.

## Two-Layer Safety

```
Local (pre-commit hook)     GitHub (CI pipeline)
─────────────────────────   ─────────────────────
Fast feedback loop          Final safety net
Blocks bad commits          Blocks bad merges
Can be skipped (--no-verify)  Cannot be skipped
```

## Future Optimizations (Not In Scope)

- **Parallel execution:** Run backend + frontend with `&` + `wait` to cut time in half
- **Lightweight mode:** Replace `npm run build` with just `tsc --noEmit` for faster commits
- **lint-staged:** Run ESLint only on staged files (not full project)
- **lefthook migration:** If parallel + YAML config becomes desirable

## Implementation Steps

1. Install Husky as devDependency in frontend
2. Initialize Husky with `.husky/` directory
3. Add `prepare` script to `package.json`
4. Write the `pre-commit` hook script
5. Test: make a commit with clean code (should pass)
6. Test: introduce a type error, try to commit (should block)
