# DataPipe

**Lightweight message pipeline framework for .NET**

DataPipe lets you express application use-cases as explicit, composable pipelines built from small filters.  
It reduces boilerplate, keeps workflows readable, and works in any .NET host.

---

## Install

```bash
dotnet add package SCB.DataPipe
```

## SQL Support

```bash
dotnet add package SCB.DataPipe.Sql
```

## Example

```csharp
var pipe = new DataPipe<TestMessage>();

pipe.Use(new ExceptionAspect<TestMessage>());
pipe.Add(async m => m.Number++);

await pipe.Invoke(new TestMessage());
```

## Key Points

- Pipelines are explicit and readable
- Messages are mutable by design
- Filters are composable (lambdas or classes)
- Fully async, stateless, and thread-safe
- Works in Web APIs, console apps, background services

## Documentation

Full documentation and examples:
👉 https://github.com/SteveBate/DataPipe

## License

MIT