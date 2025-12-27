# DataPipe

DataPipe lets you express application use-cases as explicit, composable pipelines built from small filters.  
It reduces boilerplate, keeps workflows readable, and works in any .NET host.

## Why DataPipe?

- Explicit control flow (conditions, retries, grouping)
- No hidden handlers or global behavior
- Fully async, stateless, thread-safe
- Designed for integration and application services
- Developed and refined over more than a decade in real production systems

## A simple example

```csharp
var pipe = new DataPipe<OrderMessage>();
pipe.Use(new ExceptionAspect<OrderMessage>());
pipe.Add(
    new ValidateOrder(),
    new OpenSqlConnection<OrderMessage>(
        new SaveOrder(),
        new PublishAudit()
    ),
    new IfTrue<OrderMessage>(m => m.RequiresApproval,
        new SendApprovalRequest()));
```

That’s it. No more.

---

## Features

- **Composable Filters** chain together any number of filters to build workflows that are clear and modular.  
- **Aspects for Cross-Cutting Concerns** logging, exception handling, and fully extensible.
- **Scoped Resource Management**: Open SQL connections, transactions, or other resources exactly where needed.  
- **Conditional Execution**: `Policy`, `IfTrue`, `RepeatUntil`, and `ForEach` filters make complex workflows readable. 
- **Async-Ready Retries** `OnTimeoutRetry` supports fully asynchronous retry logic with custom delay strategies.  
- **inherently concurrent** process multiple messages in parallel safely.  
- **Flow Control** stop pipeline execution safely with `PipelineExecution.Stop()`.  
- **Integration Friendly** works well with APIs, console apps, background services, WinForms, and more.  
- **Minimal Footprint** small, maintainable, and easy for teams to learn.  
- **Support Packages** DataPipe.Sql for SQL connection and transaction management, DataPipe.EntityFramework for EF DbContext transactions.  

DataPipe is **fully open-source** and ready to plug into your .NET projects today.

---

## Installation

Install via NuGet:

```bash
dotnet add package SCB.DataPipe
```

Or via the Package Manager Console:

```bash
Install-Package SCB.DataPipe
```

---

## Examples

See the included tests, and example docs for a thorough guide to using DataPipe

---

## Design Philosophy

DataPipe is intentionally small, explicit, and conservative in scope.

It is designed to orchestrate business workflows, not infrastructure concerns. Pipelines describe what happens to a message as it moves through a system, step by step, in a way that remains readable months or years later.

A DataPipe pipeline is:

- Explicit - execution order is visible and intentional
- Composable - behavior is built from small, focused filters
- Mutable by design - messages evolve as work is performed
- Technology-agnostic - no assumptions about transport, hosting model, or UI

This philosophy prioritises long-term maintainability and clarity over cleverness or abstraction density.

---

## Alignment with SOLID Principles

Although lightweight, DataPipe strongly aligns with the SOLID principles:

### Single Responsibility Principle (SRP)

Each filter performs one unit of business logic.
Validation, persistence, retries, policy decisions, and side effects are separated into discrete components.

This keeps filters small, testable, and easy to reason about.

### Open / Closed Principle (OCP)

Pipelines are extended by adding or composing filters, not by modifying existing ones.

New business rules are introduced without rewriting existing logic, reducing regression risk.

### Liskov Substitution Principle (LSP)

Filters operate on a well-defined message contract.
Any filter expecting a base message can safely operate on derived domain messages.

### Interface Segregation Principle (ISP)

Optional capabilities (retrying, committing, logging, SQL participation) are expressed via small marker interfaces rather than large, leaky abstractions.

Messages only implement what they actually need.

### Dependency Inversion Principle (DIP)

Pipelines depend on abstractions (filters and message contracts) rather than concrete implementations or frameworks.

DataPipe itself has no knowledge of HTTP, databases, queues, schedulers, or UI frameworks.

---

## Technology-Agnostic by Design

DataPipe does not care where a message comes from or where it is processed.

The same pipeline can be used in:

- ASP.NET controllers
- Background services
- Console applications
- Windows services
- Message consumers
- Batch jobs

This is possible because DataPipe operates purely on in-memory messages and explicit execution flow.

There is no dependency on:

- HTTP pipelines
- Middleware frameworks
- Messaging infrastructure
- Hosting models
- UI patterns

As a result, pipelines describe business processes, not technical plumbing.

---

## Mutable Messages (By Design)

In DataPipe, messages are intentionally mutable.

A message represents a unit of work in progress, not an immutable event.
As it passes through the pipeline, filters enrich, validate, transform, and annotate it.

This approach:

- Makes workflows easy to follow
- Avoids excessive object creation
- Reflects real-world business processes
- Keeps state changes explicit and discoverable

Rather than returning new objects at every step, each filter documents its effect by mutating the message in place.

This is not accidental - it is a deliberate design choice to favour clarity over ceremony.

---

## What DataPipe Is Not

DataPipe deliberately does not attempt to be:

- A message bus or mediator
- A workflow engine
- A rules engine
- A pub/sub abstraction
- A parallel execution framework
- A scheduler

Its purpose is to orchestrate in-process business workflows in a clear, maintainable way. It’s about declaring behaviour, not executing code

---

## License
[MIT License](http://opensource.org/licenses/MIT)