# Skill: Mermaid Diagram Generation
# Scope: Workspace (Project-wide)
# Purpose: Produce consistent, readable Mermaid diagrams for DDD analysis, architecture, and flow documentation.

---

## Trigger
Use this skill whenever you need to generate **any Mermaid diagram** in this project.
Applicable in: analysis docs, architecture docs, README, API specs, and agent outputs.

---

## Diagram Type Selection

Choose the diagram type based on intent:


| Intent                              | Diagram Type         | Mermaid Keyword         |
| ----------------------------------- | -------------------- | ----------------------- |
| Bounded Context / component map     | Flowchart (TB or LR) | `graph TD` / `graph LR` |
| Domain object state transitions     | State Diagram        | `stateDiagram-v2`       |
| Cross-system interaction / API flow | Sequence Diagram     | `sequenceDiagram`       |
| Entity relationships / DB schema    | ER Diagram           | `erDiagram`             |
| Class structure / interfaces        | Class Diagram        | `classDiagram`          |
| Deployment / infrastructure         | Flowchart (LR)       | `graph LR`              |
| Timeline / process steps            | Flowchart (TD)       | `graph TD`              |

---

## Mandatory Rules (ALL diagrams must follow)

### 1. Language
- Node labels and edge labels: **English** (technical terms).
- For multilingual support, use optional annotations in parentheses or quotes.
- Example: `HC["Health Check Context (Monitoring)"]`

### 2. Structure
- Always wrap related nodes in `subgraph` blocks for grouping (flowcharts).
- Use meaningful, concise node IDs (2-4 uppercase letters or camelCase): `HC`, `AlertMgr`, `OrderSvc`.
- Max **3 levels** of subgraph nesting.

### 3. Edge Labels
- Always include edge labels on arrows to explain the relationship or message.
- Format: `A -->|"action / event"| B`
- Avoid bare unlabeled arrows unless relationship is self-evident (parent→child hierarchy).
- **Line breaks in node labels**: Use `<br/>` inside quoted labels — `\ n` is **not** reliably supported across renderers.
  ```
  ✅ CORRECT:  ZSS["ZeroMQ Subscriber<br/>(XSUB)"]
  ❌ WRONG:    ZSS["ZeroMQ Subscriber\nXSUB"]
  ```
- **Edge labels cannot span multiple lines**: `-->|"line1\nline2"|` does NOT render as two lines. Keep edge labels to a single concise phrase.

### 4. State Diagrams (`stateDiagram-v2`)
- Always define an explicit `[*]` initial state and `[*]` terminal state.
- Annotate every transition with the triggering event: `StateA --> StateB : EventName`
- Group compound states using `state "Label" { ... }` blocks when needed.
- **NEVER** add a bare `[*] --> [*]` line — it creates an invalid self-loop on the terminal pseudo-state and breaks rendering.
- **Notes syntax**: Use the multi-line block form only. Inline `note right of X : text` is **NOT supported** in `stateDiagram-v2` and will cause a parse error.
  ```
  ✅ CORRECT:
  note right of StateName
    annotation text
  end note

  ❌ WRONG (causes parse error):
  note right of StateName : annotation text
  ```

### 5. Sequence Diagrams (`sequenceDiagram`)
- Declare all participants at the top with `participant` keyword.
- Use `+` / `-` activation bars for request/response pairs.
- Wrap async operations in `par` or `loop` blocks when applicable.
- Include `Note over X,Y: description` for important domain rules or invariants.
- **`\n` is NOT a line break** in `Note over` text or message labels. Keep them to a single line or split into two separate Note statements.

### 6. ER Diagrams (`erDiagram`)
- Use standard crow's foot notation: `||--o{`, `}o--||`, etc.
- Always annotate relationship lines with a verb phrase: `PLACES`, `CONTAINS`, `TRIGGERS`.

### 7. Class Diagrams (`classDiagram`)
- Prefix interfaces with `<<interface>>` stereotype.
- Show only public members relevant to the design; omit implementation details.
- Use `<|--` for inheritance, `..>` for dependency, `o--` for aggregation.

### 8. Formatting & Readability
- Add a `%%` comment line above each diagram block describing its purpose.
- Keep diagrams **focused**: one diagram per concern. Split large diagrams rather than cramming.
- After the closing code fence, add a `> **Design Intent**:` blockquote explaining why the diagram is structured this way.

---

## Step-by-Step Workflow

1. **IDENTIFY** the diagram type from the table above.
2. **DRAFT** node IDs and labels.
3. **APPLY** all Mandatory Rules for the chosen type.
4. **WRITE** the mermaid code block.
5. **ADD** a `> Design Intent` blockquote immediately after the diagram.
6. **VALIDATE**: Check for unlabeled edges, missing `[*]` states, and undeclared participants.

---

## Quality Checklist (Run before finalizing)

- [ ] Correct diagram type chosen for the intent.
- [ ] All nodes have meaningful IDs and readable labels.
- [ ] All edges have labels (flowchart / state / sequence).
- [ ] `subgraph` used for logical grouping (flowchart).
- [ ] `[*]` initial and terminal states present (stateDiagram).
- [ ] No bare `[*] --> [*]` line in stateDiagram-v2.
- [ ] Notes in stateDiagram-v2 use block form (`note right of X` / `end note`), NOT inline colon form.
- [ ] Node labels use `<br/>` for line breaks, NOT `\n`.
- [ ] Edge labels (flowchart) and Note/message labels (sequence) are single-line.
- [ ] All participants declared (sequenceDiagram).
- [ ] Design Intent blockquote written after the diagram.
- [ ] No more than 3 subgraph nesting levels.
- [ ] Diagram renders without syntax errors.

---

## Example: Flowchart (Bounded Context Map)

```mermaid
%% Bounded Context Map — System Overview
graph TD
    subgraph "Core Domain"
        HC["Health Check Context"]
        AL["Alert Context"]
    end
    subgraph "Supporting Domain"
        MF["Market Feed Context"]
    end
    MF -->|"Feed Status Changed"| HC
    HC -->|"Threshold Exceeded"| AL
