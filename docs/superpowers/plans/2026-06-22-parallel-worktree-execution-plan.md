# Plan: Parallel Worktree Execution

**Generated**: 2026-06-22  
**Estimated Complexity**: High

## Overview

Finish the remaining TEC FuelMix hardening work by splitting independent tasks into child Codex worktrees. The main worktree owns coordination, Task 3 cleanup, review gates, and final integration.

## Prerequisites

- Main branch state includes completed Tasks 1 and 2 plus Task 3 migrator commits.
- Each child worktree starts from the current working tree and commits its lane.
- Child lanes must not rewrite shared history or revert unrelated changes.

## Sprint 1: Parallel Lanes

**Goal**: Make progress on independent remaining tasks before final integration.

**Demo/Validation**:
- Each child thread reports worktree path, branch, commit SHA, changed files, and verification output.
- Main thread integrates only reviewed commits.

### Task 1.1: Main Thread Task 3 Closeout

- **Location**: `infra/terraform`, `README.md`, `src/TecFuelMix.DbMigrator`
- **Description**: Fix the DB username contract so DbUp app roles match Terraform runtime users.
- **Dependencies**: Existing Task 3 migrator commits.
- **Acceptance Criteria**:
  - Terraform no longer exposes app DB usernames as overridable variables.
  - Terraform and DbUp both use `fuelmix_writer` and `fuelmix_reader`.
  - README states role names are fixed and passwords remain configurable.
- **Validation**:
  - `dotnet build .\TecFuelMix.sln`
  - `terraform -chdir=infra/terraform validate`

### Task 1.2: Child Lane Task 4

- **Location**: `tests/TecFuelMix.Tests`
- **Description**: Add Testcontainers PostgreSQL and Respawn fixture, then migrate repository integration tests away from fixed host port `55432`.
- **Dependencies**: None beyond current branch.
- **Acceptance Criteria**:
  - Repository tests own an isolated PostgreSQL container.
  - Tests reset state through Respawn.
- **Validation**:
  - `dotnet test .\tests\TecFuelMix.Tests\TecFuelMix.Tests.csproj --filter FuelMixRepositoryTests`

### Task 1.3: Child Lane Task 5

- **Location**: `src/TecFuelMix.Core`, `src/TecFuelMix.ReadApiLambda`, `tests/TecFuelMix.Tests`
- **Description**: Add bounded read queries and expanded read API routes.
- **Dependencies**: May need light merge handling with Task 4 tests.
- **Acceptance Criteria**:
  - `/fuel-mix/latest`, `/fuel-mix`, `/fuel-mix/categories`, `/ingestion-runs/latest`, and `/health` are covered.
  - History route enforces 7-day and 500-row limits.
- **Validation**:
  - `dotnet test .\TecFuelMix.sln`

### Task 1.4: Child Lane Task 6

- **Location**: `src/TecFuelMix.Core`, `src/TecFuelMix.ReadApiLambda`, `tests/TecFuelMix.Tests`
- **Description**: Add read API Lambda authorizer and runtime DB secret loading.
- **Dependencies**: Coordinate with Task 5 if both touch read API handler tests.
- **Acceptance Criteria**:
  - Bearer token authorizer allows/denies expected tokens.
  - DB connection factory supports local connection-string fallback and Secrets Manager runtime path.
- **Validation**:
  - `dotnet test .\TecFuelMix.sln`

### Task 1.5: Child Lane Task 7

- **Location**: Lambda project files and handlers
- **Description**: Add Powertools logging/metrics without logging raw payloads, tokens, passwords, or connection strings.
- **Dependencies**: May need merge handling with Task 5/6 handler edits.
- **Acceptance Criteria**:
  - Lambda projects reference Powertools packages.
  - Metrics are emitted around fetch/write/read outcomes.
- **Validation**:
  - `dotnet test .\TecFuelMix.sln`

### Task 1.6: Child Lane Task 8

- **Location**: `infra/terraform`
- **Description**: Harden Terraform secrets, auth, dependencies, log retention, alarms, and example variables.
- **Dependencies**: Coordinate with Task 3 fixed DB role names and Task 6 runtime env names.
- **Acceptance Criteria**:
  - Lambda DB env vars use host/database/secret ARN, not password-bearing connection strings.
  - Lambda authorizer, log retention, extra alarms, explicit dependencies, and `terraform.tfvars.example` exist.
- **Validation**:
  - `terraform -chdir=infra/terraform fmt -check`
  - `terraform -chdir=infra/terraform validate`

## Sprint 2: Integration

**Goal**: Bring reviewed child commits back to the main worktree.

**Demo/Validation**:
- Main worktree has one coherent history with each completed task committed.
- Full test/build/Terraform checks pass.

### Task 2.1: Integrate Child Lanes

- **Location**: whole repo
- **Description**: Merge or cherry-pick child commits in dependency order: Task 3 closeout, Task 4, Task 5, Task 6, Task 7, Task 8.
- **Dependencies**: Child thread completion.
- **Acceptance Criteria**:
  - Merge conflicts are resolved without dropping child verification coverage.
  - Each integrated task keeps its own commit or clear merge commit.
- **Validation**:
  - `dotnet test .\TecFuelMix.sln`
  - `terraform -chdir=infra/terraform fmt -check`
  - `terraform -chdir=infra/terraform validate`

### Task 2.2: Evidence And Final Docs

- **Location**: `README.md`, `docs/evidence`
- **Description**: Refresh evidence files and README runbook after integration.
- **Dependencies**: All implementation lanes integrated.
- **Acceptance Criteria**:
  - Evidence files match current HEAD.
  - README describes implemented behavior only.
- **Validation**:
  - `git diff --check`
  - full command evidence in `docs/evidence`

## Testing Strategy

- Child lanes run their narrow checks before committing.
- Main thread reruns full `dotnet test`, Terraform format/validate, Docker builds where evidence requires them, and final `git diff --check`.

## Potential Risks & Gotchas

- Task 5, 6, and 7 all touch Lambda handlers; expect merge conflicts.
- Task 8 must align with Task 6 environment variable names.
- Child worktrees created before Task 3 closeout may need to rebase or manually apply the fixed DB username contract.

## Rollback Plan

- Each lane is isolated in its own worktree and commit.
- If a lane is not ready, skip its integration and keep the main branch on the last reviewed commit.
