# Pre-Commit Build Hook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set up Husky so that `dotnet build`, `dotnet test`, `tsc --noEmit`, and `npm run build` all pass before any commit is allowed.

**Architecture:** Install Husky v9 as a devDependency in the frontend project. The `prepare` script wires Git to use `.husky/` as the hooks directory. A single `pre-commit` shell script runs backend and frontend checks sequentially with `set -e`.

**Tech Stack:** Husky v9, Git hooks, Bash shell script

---

### Task 1: Install Husky

**Files:**
- Modify: `frontend/package.json` (devDependencies + scripts.prepare)

- [ ] **Step 1: Install Husky as a devDependency**

Run from `menunest/frontend/`:

```bash
npm install husky --save-dev
```

Expected: `husky` added to `devDependencies` in `package.json`, `package-lock.json` updated.

- [ ] **Step 2: Add the `prepare` script to package.json**

Open `frontend/package.json` and add `prepare` to the `scripts` block:

```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "lint": "eslint .",
    "preview": "vite preview",
    "prepare": "cd .. && husky frontend/.husky"
  }
}
```

**Why `cd ..`?** The `package.json` lives in `frontend/`, but Git's root is `menunest/`. Husky needs to run from the Git root to register the hooks directory. The argument `frontend/.husky` tells Husky where the hook scripts live, relative to the Git root.

- [ ] **Step 3: Run prepare to initialize Husky**

Run from `menunest/frontend/`:

```bash
npm run prepare
```

Expected: A `.husky/` directory is created at `menunest/frontend/.husky/`. Git's `core.hooksPath` is now set to `frontend/.husky`.

- [ ] **Step 4: Verify Git hooks path is configured**

Run from `menunest/`:

```bash
git config core.hooksPath
```

Expected output:

```
frontend/.husky
```

- [ ] **Step 5: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/.husky/
git commit -m "chore: install husky and configure prepare script"
```

---

### Task 2: Write the pre-commit hook script

**Files:**
- Create: `frontend/.husky/pre-commit`

- [ ] **Step 1: Create the pre-commit hook file**

Create `frontend/.husky/pre-commit` with this exact content:

```bash
#!/bin/sh
set -e

echo "=== Pre-commit: Backend build ==="
cd backend
dotnet build --no-restore --configuration Release

echo "=== Pre-commit: Backend tests ==="
dotnet test --no-build --configuration Release --verbosity minimal

echo "=== Pre-commit: Frontend typecheck ==="
cd ../frontend
npx tsc --noEmit

echo "=== Pre-commit: Frontend build ==="
npm run build

echo "=== All checks passed ==="
```

**Key details:**
- `set -e` makes the script exit immediately when any command returns non-zero. Without this, a failing `dotnet build` would not block the commit.
- `echo` lines give the developer clear progress feedback during the ~20-40s the hook runs.
- The working directory when Git invokes the hook is the Git root (`menunest/`), so `cd backend` works correctly.

- [ ] **Step 2: Make the script executable**

Run from `menunest/`:

```bash
chmod +x frontend/.husky/pre-commit
```

- [ ] **Step 3: Verify the script exists and is executable**

Run from `menunest/`:

```bash
ls -la frontend/.husky/pre-commit
```

Expected: The file exists and has execute permission (`-rwxr-xr-x` or similar).

- [ ] **Step 4: Commit**

```bash
git add frontend/.husky/pre-commit
git commit -m "feat: add pre-commit hook for backend and frontend build checks"
```

---

### Task 3: Test — clean commit should pass

- [ ] **Step 1: Make a trivial change to test with**

Add a blank comment to any file. For example, add a comment to `frontend/src/main.tsx`:

```typescript
// pre-commit hook test
```

- [ ] **Step 2: Stage and commit**

```bash
git add frontend/src/main.tsx
git commit -m "test: verify pre-commit hook passes on clean code"
```

Expected output (abbreviated):

```
=== Pre-commit: Backend build ===
Build succeeded.
=== Pre-commit: Backend tests ===
Passed!
=== Pre-commit: Frontend typecheck ===
=== Pre-commit: Frontend build ===
vite v8.x building for production...
✓ built in Xs
=== All checks passed ===
[feat/ai-assistant abcdef0] test: verify pre-commit hook passes on clean code
```

The commit should succeed.

- [ ] **Step 3: Revert the test change**

```bash
git revert HEAD --no-edit
```

---

### Task 4: Test — broken code should block commit

- [ ] **Step 1: Introduce a deliberate TypeScript error**

In `frontend/src/main.tsx`, add a line that will fail type-checking:

```typescript
const x: number = "not a number";
```

- [ ] **Step 2: Try to commit — expect failure**

```bash
git add frontend/src/main.tsx
git commit -m "test: this should be blocked by pre-commit"
```

Expected: The commit is **blocked**. Output includes:

```
=== Pre-commit: Backend build ===
Build succeeded.
=== Pre-commit: Backend tests ===
Passed!
=== Pre-commit: Frontend typecheck ===
error TS2322: Type 'string' is not assignable to type 'number'.
```

The commit does NOT proceed.

- [ ] **Step 3: Remove the deliberate error**

Remove the `const x: number = "not a number";` line from `frontend/src/main.tsx`.

```bash
git checkout -- frontend/src/main.tsx
```

- [ ] **Step 4: Verify --no-verify escape hatch works**

Temporarily re-add the error, then commit with `--no-verify`:

```typescript
const x: number = "not a number";
```

```bash
git add frontend/src/main.tsx
git commit --no-verify -m "test: --no-verify bypasses hook"
```

Expected: The commit succeeds despite the type error because the hook was skipped.

- [ ] **Step 5: Revert and clean up**

```bash
git revert HEAD --no-edit
```

All test commits are reverted. The hook is fully working.
