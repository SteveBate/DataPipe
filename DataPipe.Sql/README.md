# DataPipe.Sql

**SQL helpers for DataPipe pipelines**

Provides filters for managing SQL connections and transactions in DataPipe pipelines.

---

## Install

```bash
dotnet add package SCB.DataPipe.Sql
```

## Example

```csharp
var pipe = new DataPipe<TestMessage>();

pipe.Use(new ExceptionAspect<TestMessage>());

pipe.Run(
    new StartTransaction<TestMessage>(
        new OpenSqlConnection<TestMessage>(
            new SaveOrder()
        )
    )
);

await pipe.Invoke(new TestMessage());
```

## Key Points

- Scoped SQL connections and transactions
- Works seamlessly with DataPipe pipelines
- Fully async and thread-safe

## License

MIT