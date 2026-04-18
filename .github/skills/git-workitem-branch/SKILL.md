---
name: git-workitem-branch
description: 'Manage Git branches by WorkItemID from a Scrum backlog CSV, ensuring parent branches exist before child branches, developing in order, committing per item, and merging to parent on completion'
---

# Git Work Item Branch Workflow

Your goal is to implement backlog work items in order using a structured Git branching strategy derived from a Scrum backlog CSV (e.g., `BrokerageMonitoringPlatform.backlog.csv`).

## Branch Naming Convention

Map each WorkItemID to a branch name using lowercase kebab-case:

| WorkItemID | Branch Name         |
| ---------- | ------------------- |
| `EP-001`   | `ep/ep-001`         |
| `US-001`   | `us/us-001`         |

- Epic branches: `ep/{id}` — branched from `arch` (or the project integration branch)
- Story branches: `us/{id}` — branched from their parent Epic branch `ep/{parent-id}`

## Step-by-Step Workflow

### Step 1 — Resolve Parent Branch

Before creating any branch, check whether its parent branch already exists:

```powershell
git branch --list "ep/{parent-id}"
```

- If the parent branch **does not exist**, create it first (recursively apply this same workflow for the parent).
- If the parent branch **exists**, proceed to Step 2.

### Step 2 — Create the Work Item Branch

Switch to the parent branch and create the work item branch from it:

```powershell
git checkout ep/{parent-id}
git checkout -b us/{id}
```

For Epic branches, create from the project integration branch (e.g., `arch`):

```powershell
git checkout arch
git checkout -b ep/{id}
```

### Step 3 — Implement in Order

Process work items in the sequence defined by the backlog CSV (ordered by `WorkItemId`).

- Do **not** skip items or reorder them.
- Implement one work item at a time; complete it fully before moving to the next.
- Follow all `AcceptanceCriteria` in the CSV row before marking an item done.

### Step 4 — Commit After Each Item

After completing each work item, stage and commit on its branch. Do **not** push:

```powershell
git add -A
git commit -m "{type}({scope}): {short description}

- Implements {WorkItemId}: {Title}
- Acceptance criteria met: {summary}
- FR references: {FRReference}"
```

Commit message conventions:
- `type`: `feat` (new feature), `fix` (bug fix), `refactor`, `test`, `chore`
- `scope`: the WorkItemId in lowercase, e.g., `us-001`
- Keep the subject line ≤ 72 characters

**Never push** (`git push`) during development. Push only after explicit instruction.

### Step 5 — Merge to Parent on Completion

Once all Story branches under an Epic are committed, merge them into the Epic branch in order:

```powershell
git checkout ep/{parent-id}
git merge --no-ff us/{id} -m "merge(ep-{parent-id}): integrate us/{id} - {Title}"
```

Use `--no-ff` to preserve branch history.

After all Epics under the integration branch are merged:

```powershell
git checkout arch
git merge --no-ff ep/{id} -m "merge(arch): integrate ep/{id} - {EpicTitle}"
```

## Decision Rules

| Condition                              | Action                                               |
| -------------------------------------- | ---------------------------------------------------- |
| Parent branch missing                  | Create parent branch first (recursive)               |
| Work item has no ParentId              | Branch from project integration branch (`arch`)      |
| Work item already has a branch         | Switch to existing branch; do not recreate           |
| Acceptance criteria not fully met      | Do not commit; continue implementing                 |
| All stories under an epic are done     | Merge all story branches into epic branch in order   |
| Merge conflict                         | Resolve manually, then commit the resolution         |

## Quality Checklist

- [ ] Parent branch exists before creating child branch
- [ ] Branch name matches convention (`ep/` or `us/` prefix + lowercase ID)
- [ ] Work items processed in CSV order (by WorkItemId)
- [ ] Each commit is scoped to exactly one work item
- [ ] Commit message references WorkItemId, Title, and FRReference
- [ ] No `git push` executed during development
- [ ] `--no-ff` used for all merges to preserve history
- [ ] All AcceptanceCriteria verified before committing
- [ ] Merge commit message identifies the integrated branch and title

## Example Sequence

Given backlog rows: `EP-001 → US-001, US-002, US-003`:

```powershell
# 1. Create Epic branch
git checkout arch
git checkout -b ep/ep-001

# 2. Create first Story branch
git checkout ep/ep-001
git checkout -b us/us-001

# 3. Implement US-001, then commit
git add -A
git commit -m "feat(us-001): establish solution architecture and project references

- Implements US-001: 建立 Solution 架構與專案參考
- Acceptance criteria met: BrokerageMonitor.sln created, 7 projects, dotnet build OK
- FR references: N/A"

# 4. Merge US-001 back to EP-001
git checkout ep/ep-001
git merge --no-ff us/us-001 -m "merge(ep-001): integrate us/us-001 - Solution Architecture"

# 5. Create next Story branch from EP-001
git checkout -b us/us-002
# ... implement and commit ...

# 6. After all stories done, merge EP-001 to arch
git checkout arch
git merge --no-ff ep/ep-001 -m "merge(arch): integrate ep/ep-001 - Project Foundation"
```
