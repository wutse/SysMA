---
name: git-workitem-flow
description: 'Manage Git branches by WorkItemID from a Scrum backlog CSV, ensuring parent branches exist before child branches, developing in order, committing per item, and merging to parent on completion'
---

# Git Work Item Branch Workflow

Your goal is to implement backlog work items in order using a structured Git branching strategy derived from a Scrum backlog CSV (e.g., `BrokerageMonitoringPlatform.backlog.csv`).

## Branch Naming Convention

Map each WorkItemID to a branch name using lowercase kebab-case:

| WorkItemID | Branch Name         |
| ---------- | ------------------- |
| `EP-001`   | `ep-001`         |
| `US-001`   | `us-001`         |

- Epic branches: `{id}` — branched from `arch` (or the project integration branch)
- Story branches: `{id}` — branched from their parent Epic branch `{parent-id}`

## Step-by-Step Workflow

### Step 1 — Resolve Parent Branch

Before creating any branch, check whether its parent branch already exists:

```powershell
git branch --list "{parent-id}"
```

- If the parent branch **does not exist**, create it first (recursively apply this same workflow for the parent).
- If the parent branch **exists**, proceed to Step 2.

### Step 2 — Create the Work Item Branch

Switch to the parent branch and create the work item branch from it:

```powershell
git checkout {parent-id}
git checkout -b {id}
```

For Epic branches, create from the project integration branch (e.g., `arch`):

```powershell
git checkout arch
git checkout -b {id}
```

### Step 3 — Check for Child Items (Depth-First)

**Before implementing the current work item**, scan the backlog CSV for rows whose `ParentId` matches the current `WorkItemId`.

- If **child items exist**: recursively apply Steps 1–6 for **each child** (in CSV order) before implementing the current item.
  - Create the child branch (from the current branch).
  - Recurse into the child's own children, if any.
  - Implement and commit the child.
  - Merge the child back to the current branch.
- If **no child items exist**: proceed directly to Step 4 (implementation).

This ensures a **depth-first** traversal: leaves are implemented and merged up before their parents.

### Step 4 — Implement in Order

Process work items in the sequence defined by the backlog CSV (ordered by `WorkItemId`).

- Do **not** skip items or reorder them.
- Implement one work item at a time; complete it fully before moving to the next.
- Follow all `AcceptanceCriteria` in the CSV row before marking an item done.

### Step 5 — Commit After Each Item

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

### Step 6 — Merge to Parent on Completion

Once all Story branches under an Epic are committed, merge them into the Epic branch in order:

```powershell
git checkout {parent-id}
git merge --no-ff {id} -m "merge({parent-id}): integrate {id} - {Title}"
```

Use `--no-ff` to preserve branch history.

After all Epics under the integration branch are merged:

```powershell
git checkout arch
git merge --no-ff {id} -m "merge(arch): integrate {id} - {EpicTitle}"
```

## Decision Rules

| Condition                              | Action                                               |
| -------------------------------------- | ---------------------------------------------------- |
| Parent branch missing                  | Create parent branch first (recursive)               |
| Work item has no ParentId              | Branch from project integration branch (`arch`)      |
| Work item already has a branch         | Switch to existing branch; do not recreate           |
| Work item has child items in CSV       | Create branch, then recursively process children (depth-first) before implementing current item |
| Acceptance criteria not fully met      | Do not commit; continue implementing                 |
| All stories under an epic are done     | Merge all story branches into epic branch in order   |
| Merge conflict                         | Resolve manually, then commit the resolution         |

## Quality Checklist

- [ ] Parent branch exists before creating child branch
- [ ] Branch name matches convention (lowercase ID)
- [ ] Child items checked before implementing any work item
- [ ] Children processed depth-first (all descendants done before parent)
- [ ] Work items processed in CSV order (by WorkItemId) within the same level
- [ ] Each commit is scoped to exactly one work item
- [ ] Commit message references WorkItemId, Title, and FRReference
- [ ] No `git push` executed during development
- [ ] `--no-ff` used for all merges to preserve history
- [ ] All AcceptanceCriteria verified before committing
- [ ] Merge commit message identifies the integrated branch and title

## Example Sequence

Given backlog rows: `EP-001 → US-001 → TS-001, TS-002; US-002`:

```powershell
# 1. Start EP-001 — create Epic branch
git checkout arch
git checkout -b ep-001

# 2. EP-001 has children (US-001, US-002) → process children first (depth-first)

# 3. Start US-001 — create Story branch from EP-001
git checkout ep-001
git checkout -b us-001

# 4. US-001 has children (TS-001, TS-002) → process children first

# 5. Start TS-001 — create Task branch from US-001
git checkout us-001
git checkout -b ts-001
# TS-001 has no children → implement directly
git add -A
git commit -m "feat(ts-001): ...
- Implements TS-001: ..."

# 6. Merge TS-001 back to US-001
git checkout us-001
git merge --no-ff ts-001 -m "merge(us-001): integrate ts-001 - ..."

# 7. Start TS-002 — create Task branch from US-001
git checkout -b ts-002
# implement and commit ...
git checkout us-001
git merge --no-ff ts-002 -m "merge(us-001): integrate ts-002 - ..."

# 8. All children of US-001 done → now implement US-001 itself, then commit
git checkout us-001
git add -A
git commit -m "feat(us-001): ...
- Implements US-001: ..."

# 9. Merge US-001 back to EP-001
git checkout ep-001
git merge --no-ff us-001 -m "merge(ep-001): integrate us-001 - ..."

# 10. Start US-002 — no children → implement directly
git checkout -b us-002
git add -A
git commit -m "feat(us-002): ..."
git checkout ep-001
git merge --no-ff us-002 -m "merge(ep-001): integrate us-002 - ..."

# 11. All children of EP-001 done → merge EP-001 to arch
git checkout arch
git merge --no-ff ep-001 -m "merge(arch): integrate ep-001 - Project Foundation"
```
