# Senior C# Developer Implementation Standards

This document defines the architectural standards and coding practices for the `src/` directory, focusing on Clean Code, Domain-Driven Design (DDD), and high-performance Asynchronous Programming.

---

## 1. Core Principles (SOLID + Least Knowledge)

All implementations must adhere to the six fundamental pillars of Object-Oriented Design:

1.  **S - Single Responsibility Principle (SRP)**: A class should have only one reason to change. Use `MediatR` handlers to isolate business logic.
2.  **O - Open/Closed Principle (OCP)**: Software entities should be open for extension but closed for modification. Leverage the **Strategy** and **Decorator** patterns to add behavior.
3.  **L - Liskov Substitution Principle (LSP)**: Objects of a superclass should be replaceable with objects of its subclasses without breaking the application.
4.  **I - Interface Segregation Principle (ISP)**: Clients should not be forced to depend on methods they do not use. Keep interfaces lean and focused.
5.  **D - Dependency Inversion Principle (DIP)**: Depend on abstractions, not concretions.
6.  **LoD - Law of Demeter**: Minimize coupling. An object should only communicate with its immediate friends; avoid "train wrecks" like `order.Customer.Profile.Address.City`.

---

## 2. DDD & Encapsulation Rules

To protect the integrity of the Domain Model:

*   **Private Setters**: All Entity and Aggregate Root properties must have `private` or `init` setters. State changes must occur via explicit domain methods (e.g., `RenameItem(...)` instead of `Name = "..."`).
*   **Object Creation**: Direct instantiation via `new` is discouraged for complex entities. Use **Constructors** with validation or **Factory Methods** to ensure the object is always in a valid state.

---

## 3. Design Pattern Standards

Replace procedural logic with robust design patterns:

*   **Strategy Pattern**: Use to eliminate complex `if/else` or `switch` blocks when multiple algorithms or behaviors exist for a single task.
*   **Factory Pattern**: Centralize the logic for creating complex objects or hierarchies.
*   **Decorator Pattern**: Implement cross-cutting concerns such as **Logging**, **Caching**, or **Validation** without polluting the core business logic.
*   **MediatR (CQRS)**: Use to decouple the entry point (API/UI) from the domain logic by separating Reads (Queries) and Writes (Commands).

---

## 4. Efficiency & Async Programming

Code performance and resource management are non-negotiable:

*   **Async/Await**: All I/O-bound operations must be asynchronous. 
*   **Token Propagation**: Always accept and pass a `CancellationToken` through the entire call stack to support graceful cancellation.
*   **LINQ Best Practices**: 
    *   Avoid unnecessary memory allocations (e.g., avoid `.ToList()` until strictly necessary).
    *   Use `IQueryable` for database filtering to ensure logic is executed at the data source level.
    *   Prefer `ValueTask` for high-frequency async methods where the result is often available synchronously.

---

## 5. Directory Scope

These rules apply strictly to the implementation phase within the **`src/`** directory. All logic must be "Clean by Design" before moving to subsequent lifecycle stages.

---

## 6. Context Window Management

To prevent context degradation and maintain response quality, this agent actively monitors token usage throughout the session.

### 6.1 Monitoring Policy

After **every response**, estimate cumulative context window usage against the model's total capacity:

| Usage Level | Action |
|-------------|--------|
| < 70%       | Continue normally |
| ≥ 70%       | Trigger **Session Handoff Protocol** immediately |

### 6.2 Session Handoff Protocol

When usage reaches **70%**, execute the following steps **before generating further implementation output**:

1. **Emit a handoff summary** in the current session:
   ```
   ## Session Handoff Summary
   - Completed tasks: [list]
   - In-progress task: [task name + last known state]
   - Pending tasks: [list]
   - Key decisions made: [brief bullet points]
   - Files modified: [list with file paths]
   - Next action: [exact instruction for new session to resume]
   ```

2. **Instruct the user** to open a **new VS Code Chat session** and paste:
   ```
   /dev [paste the Session Handoff Summary above]
   Resume from: [next action]
   ```

3. **Stop** generating further implementation output in the current session to avoid truncation or context corruption.

### 6.3 Estimation Method

Use the following heuristic to estimate usage percentage after each response:

- Count accumulated turns (user + assistant messages)
- Estimate tokens: each turn ≈ average of prior message lengths
- Compare against known model limit (e.g., 200 K tokens for Claude Sonnet)
- If token estimate ≥ 70% of limit → trigger handoff

> **Note**: This is a best-effort estimate. When uncertain, prefer to trigger handoff early rather than risk context overflow.
