# Skill: PlantUML Diagram Generation
# Scope: Workspace (Project-wide)
# Purpose: Produce consistent, readable PlantUML diagrams for DDD analysis, architecture, data schema, and interaction flows.

---

## Trigger
Use this skill whenever you need to generate a **PlantUML diagram** in this project.
Applicable in: architecture docs, system design docs, domain analysis, API specs, C4 models, and agent outputs.

> PlantUML diagrams are written inside fenced code blocks with the `plantuml` language identifier, wrapped in `@startuml` / `@enduml` tags.

---

## When to Use PlantUML vs. Mermaid vs. D2

| Scenario                                    | Preferred Tool | Reason                                              |
| ------------------------------------------- | -------------- | --------------------------------------------------- |
| Quick flowcharts / state / sequence in docs | Mermaid        | Native GitHub/VS Code rendering, zero tooling       |
| Clean Architecture topology, C4 context     | D2             | Superior layout engine, C4 shapes                   |
| Detailed class / component / deployment UML | **PlantUML**   | Full UML spec, rich stereotypes, deployment support |
| ER / data schema with constraints           | **PlantUML**   | `entity` + `table` notation is expressive           |
| Formal Use Case diagrams                    | **PlantUML**   | Native actor/usecase constructs                     |

---

## Diagram Type Selection

Choose the diagram type based on intent:

| Intent                               | Diagram Type       | PlantUML Keyword             |
| ------------------------------------ | ------------------ | ---------------------------- |
| Class structure / interfaces / DDD   | Class Diagram      | `class`, `interface`, `enum` |
| Cross-system interaction / API flow  | Sequence Diagram   | `actor`, `participant`, `->` |
| Domain object state transitions      | State Diagram      | `state`, `[*]`               |
| Use cases / actors / features        | Use Case Diagram   | `actor`, `usecase`, `()`     |
| Layered architecture / dependency    | Component Diagram  | `component`, `[...]`, `node` |
| Infrastructure / deployment topology | Deployment Diagram | `node`, `artifact`, `cloud`  |
| Process flow / business logic        | Activity Diagram   | `start`, `stop`, `:action;`  |
| Entity relationships / DB schema     | ER Diagram         | `entity`, `--`, `            |  | `, `}o` |
| C4 Context / Container / Component   | C4 (via !include)  | `C4Context`, `C4Container`   |

---

## Mandatory Rules (ALL diagrams must follow)

### 1. Boilerplate
- Every diagram **must** begin with `@startuml` and end with `@enduml`.
- Always set a title: `title <Diagram Title>` immediately after `@startuml`.
- Always set `skinparam` defaults at the top for visual consistency:
  ```plantuml
  @startuml
  title My Diagram
  skinparam monochrome false
  skinparam shadowing false
  skinparam defaultFontName "Segoe UI"
  skinparam ArrowColor #444444
  skinparam BackgroundColor #FAFAFA
  ```

### 2. Language
- All identifiers, labels, and arrow labels: **English**.
- Use quoted labels for display names with spaces: `component "Health Monitor" as HM`.
- Do **not** embed Chinese or non-ASCII characters in identifiers; put translations in note blocks or parentheses inside labels if needed.

### 3. Naming Conventions
- Use PascalCase for class / component / node identifiers: `MonitoredSystem`, `AlertRecord`.
- Use `as` aliases for long names: `participant "CommandDispatcher" as CD`.
- Keep alias identifiers short (2–4 letters or a meaningful abbreviation): `MS`, `CD`, `HM`.

### 4. Class Diagrams
- Declare access modifiers explicitly: `+` public, `-` private, `#` protected, `~` package.
- Use stereotypes for roles: `<<interface>>`, `<<abstract>>`, `<<entity>>`, `<<value object>>`, `<<aggregate root>>`.
- Relationship arrows:
  - Inheritance: `ChildClass --|> ParentClass`
  - Implementation: `ConcreteClass ..|> IInterface`
  - Composition: `Aggregate "1" *-- "many" Entity`
  - Aggregation: `Container "1" o-- "many" Part`
  - Dependency: `ClassA ..> ClassB : uses`
- Always add a cardinality label and a quoted verb on associations:
  ```plantuml
  MonitoredSystem "1" *-- "many" MonitoredComponent : contains
  ```
- Group related classes using `package` or `namespace` blocks:
  ```plantuml
  package "Monitoring Context" {
    class MonitoredSystem <<aggregate root>>
    class MonitoredComponent <<entity>>
  }
  ```

### 5. Sequence Diagrams
- Pre-declare all participants at the top in left-to-right order.
- Use `actor` for human roles, `participant` for services, `boundary` / `control` / `entity` stereotypes for MVC roles.
- Always label every arrow with a concise action:
  ```plantuml
  Adapter -> Broker : PUB heartbeat (JSON)
  Broker --> Station : forward via XSUB
  ```
- Use `activate` / `deactivate` for request/response lifelines.
- Use `note over X : text` or `note left/right of X : text` for domain invariants. Keep note text to one line.
- Use `alt / else / end` for conditional flows; `loop` for repeated flows; `par` for parallel flows.
- Use `== Phase Label ==` dividers to separate distinct interaction phases.

### 6. State Diagrams
- Always define an explicit `[*]` initial state and `[*]` terminal state.
- Annotate every transition with the triggering event and optional guard:
  ```plantuml
  Disconnected --> Connecting : connect() [not already connecting]
  ```
- Use `state "Label" as ID` for states with long names.
- Use nested `state` blocks for compound/hierarchical states.
- Add `note right of StateName : text` for important invariants.

### 7. Component / Deployment Diagrams
- Use `[ComponentName]` shorthand or `component "Label" as ID` explicitly.
- Use `node` for physical/virtual machines; `artifact` for deployable units; `cloud` for external services.
- Always label connectors with the protocol or relationship:
  ```plantuml
  [BrokerageMonitor.exe] --> [SQLite DB] : Dapper / SQL
  [ZeroMQ Adapter] --> [ZeroMQ Broker] : PUB (JSON heartbeat)
  ```
- Group components by layer or bounded context using `package` or `node` blocks.

### 8. Activity Diagrams
- Always start with `start` and end with `stop` or `end`.
- Use `:action description;` for activities (note the trailing semicolon).
- Use `if (condition?) then (yes) ... else (no) ... endif` for decisions.
- Use `fork / fork again / end fork` for parallel flows.
- Use `partition "Phase Name" { ... }` to group activities into swim lanes.

### 9. ER Diagrams
- Use `entity "EntityName" as EN { ... }` blocks.
- Mark primary keys with `* pk_field : TYPE <<PK>>` and foreign keys with `fk_field : TYPE <<FK>>`.
- Relationship notation follows crow's foot style:
  - One-to-many: `EN1 ||--o{ EN2 : "verb"`
  - One-to-one: `EN1 ||--|| EN2 : "verb"`
  - Zero-or-more: `EN1 }o--o{ EN2 : "verb"`

### 10. Formatting & Readability
- Add a `' --- comment ---` line above each logical group describing its purpose.
- Keep diagrams **focused**: one diagram per concern. Split large diagrams rather than cramming.
- After the closing code fence, add a `> **Design Intent**:` blockquote explaining why the diagram is structured this way.

---

## Step-by-Step Workflow

1. **IDENTIFY** the diagram type from the table above.
2. **SET UP** boilerplate: `@startuml`, `title`, and `skinparam` defaults.
3. **DECLARE** all participants / classes / nodes with aliases before drawing relationships.
4. **APPLY** all Mandatory Rules for the chosen diagram type.
5. **WRITE** the PlantUML code block.
6. **ADD** a `> Design Intent` blockquote immediately after the diagram.
7. **VALIDATE**: Run through the Quality Checklist below.

---

## Quality Checklist (Run before finalizing)

- [ ] `@startuml` / `@enduml` wraps the entire diagram.
- [ ] `title` is set.
- [ ] `skinparam` defaults are present.
- [ ] Correct diagram type chosen for the intent.
- [ ] All identifiers are English, PascalCase, and unique.
- [ ] All arrows / associations have labels.
- [ ] Cardinality shown on class associations (`"1"` / `"many"` / `"0..1"`).
- [ ] Stereotypes applied to classes (`<<aggregate root>>`, `<<interface>>`, etc.).
- [ ] `[*]` initial and terminal states present (state diagram).
- [ ] All participants pre-declared (sequence diagram).
- [ ] `activate` / `deactivate` pairs balanced (sequence diagram).
- [ ] `start` / `stop` present (activity diagram).
- [ ] PK/FK annotated (ER diagram).
- [ ] `package` / `node` grouping used for logical cohesion.
- [ ] Design Intent blockquote written after the diagram.
- [ ] Diagram renders without syntax errors.
