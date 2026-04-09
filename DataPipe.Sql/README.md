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

// Option 1: Using TransactionScope (comes before the connection)
pipe.Add(
    new StartTransactionScope<TestMessage>(
        new OpenSqlConnection<TestMessage>(
            new SaveOrder()
        )
    )
);

// Option 2: Using SqlTransaction (comes after the connection)
pipe.Add(
    new OpenSqlConnection<TestMessage>(connectionString,
        new StartSqlTransaction<TestMessage>(
            new SaveOrder()
        )
    )
);

await pipe.Invoke(new TestMessage());
```

## Key Points

- Scoped SQL connections and transactions
- Includes both `StartTransactionScope` (`TransactionScope`) and `StartSqlTransaction` (`SqlConnection.BeginTransactionAsync`)
- Works seamlessly with DataPipe pipelines
- Fully async and thread-safe

## License

MIT