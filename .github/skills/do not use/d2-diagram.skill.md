# Skill: D2 Diagram Generation
# Scope: Workspace (Project-wide)
# Purpose: Produce consistent, readable D2 diagrams for architecture, domain modeling, data schema, and interaction flows.

---

## Trigger
Use this skill whenever you need to generate a **D2 diagram** in this project.
Applicable in: architecture docs, system design docs, API specs, infrastructure overviews, and agent outputs.

> D2 (Declarative Diagramming) diagrams are rendered as `.d2` files or embedded in Markdown as fenced code blocks with the `d2` language identifier.

---

## Diagram Type Selection

Choose the diagram type based on intent:

| Intent                               | D2 Construct                              | Key Syntax                           |
| ------------------------------------ | ----------------------------------------- | ------------------------------------ |
| System context / component map       | Containers + connections                  | `container: Label { ... }`           |
| C4 Context / Container / Component   | Containers + `shape: rectangle / hexagon` | nested containers, labeled arrows    |
| Entity-relationship / data schema    | SQL Tables                                | `shape: sql_table`                   |
| Class structure / interfaces         | UML Classes                               | `shape: class`                       |
| Interaction / request-response flow  | Sequence Diagram                          | `shape: sequence_diagram`            |
| Deployment / infrastructure topology | Containers + shapes                       | `shape: cylinder / cloud / person`   |
| State machine                        | Containers + directional connections      | `direction: right/down`, cyclic refs |

---

## Mandatory Rules (ALL diagrams must follow)

### 1. Language
- All keys, labels, and connection labels: **English**.
- Use concise labels. For multi-word labels, use quoted strings: `key: "My Label"`.
- Do **not** embed Chinese or other non-ASCII characters in shape keys (use ASCII keys, put translations in labels if needed).

### 2. Keys vs. Labels
- A shape's **key** is its identifier (used in connections). A shape's **label** is what is displayed.
- Always define an explicit label when the key is abbreviated: `lb: "Load Balancer"`.
- Connections **must reference keys**, not labels:
  ```d2
  # ✅ CORRECT
  lb: "Load Balancer"
  api: "API Service"
  lb -> api: routes
  
  # ❌ WRONG — creates new shapes instead of referencing existing ones
  Load Balancer -> API Service
  ```

### 3. Connections
- Always include a **label** on connections to describe the relationship or message:
  ```d2
  zmq_broker -> station: "XSUB forward"
  station -> monitored_sys: "ROUTER/DEALER command"
  ```
- Use appropriate arrow directions:
  - `->` unidirectional
  - `<->` bidirectional
  - `--` undirected (association)
- For chained flows, use connection chaining for readability:
  ```d2
  adapter -> broker -> station: "heartbeat"
  ```

### 4. Containers (Grouping)
- Group related shapes using containers (nested blocks):
  ```d2
  app: "BrokerageMonitor.exe" {
    presentation: "Presentation Layer" {
      hub: "MonitorHub"
      blazor: "Blazor Components"
    }
    domain: "Domain Layer" {
      aggregates: "Aggregates"
    }
  }
  ```
- Cross-container connections use dot-notation: `app.presentation.hub -> app.domain.aggregates`
- Use `_` to reference the parent container from within a child:
  ```d2
  outer: {
    inner: {
      inner -> _.sibling: "escape to parent"
    }
    sibling
  }
  ```

### 5. SQL Table Diagrams
- Use `shape: sql_table` for ERD / data schema diagrams.
- Define PK, FK, and unique constraints explicitly:
  ```d2
  monitored_systems: {
    shape: sql_table
    system_id: "TEXT" { constraint: primary_key }
    name: "TEXT"
    is_active: "INTEGER"
  }
  monitored_components: {
    shape: sql_table
    component_id: "TEXT" { constraint: primary_key }
    system_id: "TEXT" { constraint: foreign_key }
    component_type: "TEXT"
  }
  monitored_components.system_id -> monitored_systems.system_id
  ```

### 6. Sequence Diagrams
- Set `shape: sequence_diagram` on the top-level container.
- **Pre-declare all actors** at the top (order determines left-to-right position):
  ```d2
  flow: {
    shape: sequence_diagram
    adapter; broker; station; ui
    adapter -> broker: "PUB heartbeat"
    broker -> station: "XSUB forward"
    station -> ui: "SignalR push"
  }
  ```
- Use **groups** (nested containers without connections) to label phases:
  ```d2
  flow: {
    shape: sequence_diagram
    alice; bob
    auth phase: {
      alice -> bob: "login request"
      bob -> alice: "token"
    }
  }
  ```
- Use **notes** via nested objects with no connections:
  ```d2
  flow: {
    shape: sequence_diagram
    alice; bob
    alice -> bob: "hello"
    bob."Important invariant: BI-007 dedup check"
  }
  ```
- Use **spans** for activation lifelines:
  ```d2
  flow: {
    shape: sequence_diagram
    client; server
    client.t1 -> server.t1: "request"
    client.t1 <- server.t1: "response"
  }
  ```
- Use `style.stroke-dash: 5` on return messages to distinguish responses from requests.

### 7. UML Class Diagrams
- Use `shape: class` and define fields and methods as nested keys:
  ```d2
  HealthMonitorDefinition: {
    shape: class
    +DefinitionId: Guid
    +SystemId: string
    +DeadlineTime: TimeOnly
    +WatchedComponents: "IReadOnlyList<WatchedComponent>"
    +EvaluateDeadline(): void
  }
  ```
- Show relationships with labeled connections:
  - Inheritance: `Child -> Parent: "extends" { target-arrowhead.shape: triangle }`
  - Composition: use `diamond` source arrowhead

### 8. Shape Catalog Reference
Use built-in shapes to convey semantics:

| Shape keyword | Use for                        |
| ------------- | ------------------------------ |
| `rectangle`   | General service / component    |
| `cylinder`    | Database / storage             |
| `cloud`       | External / SaaS service        |
| `person`      | Actor / user                   |
| `hexagon`     | Application / process boundary |
| `diamond`     | Decision point                 |
| `queue`       | Message queue / broker         |
| `stored_data` | Persistent data store          |
| `package`     | Module / library               |

### 9. Layout & Direction
- Set `direction` at the top level or per-container when the default layout is suboptimal:
  ```d2
  direction: right   # left-to-right flow (pipeline / sequence-style)
  direction: down    # top-to-bottom (hierarchy / dependency)
  ```
- Prefer `direction: right` for data flow / pipeline diagrams.
- Prefer `direction: down` for layered architecture (Clean Architecture layers).

### 10. Comments and Metadata
- Add a comment at the top of every diagram describing its purpose:
  ```d2
  # C4 Container Diagram — Brokerage Monitoring Platform
  # Scope: Internal deployment topology, Layer 2
  ```
- After the closing code fence in Markdown, add a `> **Design Intent**:` blockquote.

---

## Step-by-Step Workflow

1. **IDENTIFY** the diagram type from the selection table.
2. **LIST** the key shapes / actors / tables needed.
3. **DEFINE** keys with explicit labels for all shapes.
4. **GROUP** shapes into containers where applicable.
5. **DRAW** connections with labels.
6. **APPLY** the Mandatory Rules for the chosen type.
7. **SET** `direction` if the default layout is unclear.
8. **WRITE** the `d2` fenced code block.
9. **ADD** a `> Design Intent` blockquote immediately after the diagram.
10. **VALIDATE** using the Quality Checklist below.

---

## Quality Checklist (Run before finalizing)

- [ ] Correct diagram type chosen for the intent.
- [ ] Top-level comment describes the diagram purpose.
- [ ] All shape keys are ASCII, concise, and unique within scope.
- [ ] All shapes that differ from their key have explicit labels.
- [ ] All connections have labels.
- [ ] Connections reference **keys**, not display labels.
- [ ] Containers used for logical grouping where applicable.
- [ ] Sequence diagrams: actors pre-declared at top in desired order.
- [ ] Sequence diagrams: groups used to label interaction phases.
- [ ] SQL tables: PK/FK/UNQ constraints declared; FK connections between tables.
- [ ] `direction` set if layout is ambiguous.
- [ ] Design Intent blockquote written after the diagram.
- [ ] No syntax errors (test in [D2 Playground](https://play.d2lang.com/) if uncertain).

---

## Examples

### Example 1: System Context (C4 Level 1)

```d2
# C4 Context Diagram — Brokerage Monitoring Platform
# Shows top-level actors and external system boundaries

direction: right

operator: "Operator\n(Brokerage Staff)" {
  shape: person
}

station: "Brokerage Monitor Station" {
  shape: hexagon
}

zmq_broker: "ZeroMQ Broker" {
  shape: queue
}

mail_relay: "Mail Relay\n(Company SMTP)" {
  shape: cloud
}

teams: "Microsoft Teams" {
  shape: cloud
}

monitored: "Monitored Systems ×20\n(via Adapter)" {
  shape: rectangle
}

operator -> station: "views dashboard & operates"
monitored -> zmq_broker: "PUB heartbeat/status"
zmq_broker -> station: "XSUB forward"
station -> monitored: "ROUTER/DEALER command"
station -> mail_relay: "SMTP alert/summary email"
station -> teams: "Webhook notification"
```

> **Design Intent**: The station is the sole ZeroMQ consumer; the Adapter sits upstream and is out of scope. Command channel bypasses the Broker and connects directly to monitored systems.

---

### Example 2: SQL Table ERD

```d2
# ERD — Core Tables: HealthMonitorDefinitions and DailyExecutions

health_monitor_definitions: {
  shape: sql_table
  definition_id: "TEXT" { constraint: primary_key }
  system_id: "TEXT" { constraint: foreign_key }
  name: "TEXT"
  deadline_time: "TEXT"
  schedule_type: "TEXT"
  is_active: "INTEGER"
}

daily_executions: {
  shape: sql_table
  execution_id: "TEXT" { constraint: primary_key }
  definition_id: "TEXT" { constraint: [foreign_key; unique] }
  execution_date: "TEXT" { constraint: unique }
  status: "TEXT"
  created_at: "TEXT"
  evaluated_at: "TEXT"
}

daily_executions.definition_id -> health_monitor_definitions.definition_id
```

> **Design Intent**: The UNIQUE constraint on `(definition_id, execution_date)` enforces BI-012 (one instance per definition per day) at the database level.

---

### Example 3: Sequence Diagram

```d2
# Sequence Diagram — Daily Execution Creation Flow (FR-042)

creation flow: {
  shape: sequence_diagram
  quartz; creator_job; creator_svc; def_repo; exec_repo; hub

  quartz -> creator_job: "Execute (05:30 daily)"
  creator_job -> creator_svc: "CreateForDateAsync(today)"
  creator_svc -> def_repo: "GetAllActiveAsync()"
  def_repo -> creator_svc: "HealthMonitorDefinition[]"

  loop each definition: {
    creator_svc -> exec_repo: "GetByDefinitionAndDateAsync()"
    exec_repo -> creator_svc: "null (not yet created)"
    creator_svc -> exec_repo: "AddAsync(InProgress)"
    creator_svc -> hub: "PushDailyExecutionUpdated"
  }

  creator_svc -> creator_job: "done"
  creator_job -> quartz: "JobExecutionComplete"
}
```

> **Design Intent**: `DailyExecutionCreatorJob` and `AggregateHealthEvaluationJob` have separated responsibilities. The creator runs once at 05:30; the evaluator fires at each definition's individual deadline time.

---

## File Output Convention

When generating standalone D2 diagram files (not embedded in Markdown):
- Save to `docs/diagrams/{feature-name}.{diagram-type}.d2`
- Examples:
  - `docs/diagrams/BrokerageMonitor.context.d2`
  - `docs/diagrams/BrokerageMonitor.schema.d2`
  - `docs/diagrams/BrokerageMonitor.daily-execution-flow.d2`
