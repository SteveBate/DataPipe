# Entity Framework

The DataPipe.Sql package provides ready-made filters for integrating DataPipe pipelines with Microsoft.Data.SqlClient. These filters help manage database transactions and ensure data consistency during pipeline execution.

### Load user example

```csharp
var pipe = new DataPipe<LoadUserMessage>();
pipe.Use(new ExceptionAspect<LoadUserMessage>());
pipe.Add(
    new OpenSqlConnection<LoadUserMessage>(connectionString: "Data Source=...",
        new LoadUser()));

var msg = new LoadUserMessage { Id = 1, OnLog = Console.WriteLine, OnError = (e, m) => Console.WriteLine(e) };
await pipe.Invoke(msg);
Console.WriteLine(msg.Result.DisplayName);
```

### Save user example

```csharp
var pipe = new DataPipe<SaveUserMessage>();
pipe.Use(new ExceptionAspect<SaveUserMessage>());
pipe.Add(
    new StartTransaction<SaveUserMessage>(
        new OpenSqlConnection<SaveUserMessage>(connectionString: "Data Source=...",
            new SaveUser())));

var user = new User { DisplayName = "Joe Bloggs", Permissions = 4, Enabled = true };
var msg = new SaveUserMessage { Commit = true, User = user, OnLog = Console.WriteLine, OnError = (e, m) => Console.WriteLine(e) };
await pipe.Invoke(msg);
Console.WriteLine("User saved with ID: " + msg.Result.Id);
```

### The building blocks

- `OpenSqlConnection<TMessage>`: Opens a Microsoft.Data.SqlClient `SqlConnection` for the duration of the filter. The connection is available via `msg.SqlConnection`.
- `StartTransaction<TMessage>`: Starts a SQL transaction. Commits if the message completes successfully, rolls back on error or if `msg.Commit` is false.
- `ExceptionAspect<TMessage>`: Catches exceptions thrown by downstream filters, allowing for centralized error handling and logging.
- Custom filters (e.g., `LoadUser`, `SaveUser`): Implement your business logic using the provided `SqlConnection` and transaction management.

These components work together to simplify database operations within your DataPipe pipelines, ensuring that data integrity is maintained through proper transaction handling.

### Result implementation

```csharp
public class CommonResult 
{
    public bool Success { get; set; } = true;
	public string Message { get; set; } = string.Empty;
	public int StatusCode { get; set; } = 200;
}

public class UserResult : CommonResult
{
    public int Id { get; set; }
    public string DisplayName { get; set; }
    public int Permissions { get; set; }
    public bool Enabled { get; set; }
}
```

### Our custom message base

```csharp
// our custom BaseMessage that implements IUseDbContext for EF filters, and has a strongly typed Result property
public class AppContext<TResult> : BaseMessage, IUseSqlCommand where TResult : CommonResult, new()
{
    public SqlCommand Command { get; set; }
    public TResult Result { get; set; } = new TResult();
}
```

### The LoadUser message and filter

```csharp
public class LoadUserMessage : AppContext<UserResult>
{
    public int Id { get; set; }
    public string UserId { get; set; }
}

public class LoadUser : Filter<LoadUserMessage>
{
    public async Task Execute(LoadUserMessage msg)
    {
        msg.OnLog?.Invoke($"{nameof(LoadUser)}");
        
        msg.Command.CommandText = "SELECT * FROM dbo.Users WHERE Id = @Id";
		msg.Command.Parameters.Clear();
		msg.Command.Parameters.AddWithValue("@Id", msg.Id);
		using var rdr = await msg.Command.ExecuteReaderAsync();
		while(await rdr.ReadAsync())
		{
			msg.Result.Id = (int)rdr["Id"];
			msg.Result.DisplayName = (string)rdr["DisplayName"];
			msg.Result.Permissions = (int)rdr["Permissions"];
			msg.Result.Enabled = (bool)rdr["Enabled"];
		}
    }
}
```

### The SaveUser message and filter

```csharp
public class SaveUserMessage : AppContext<UserResult>, IAmCommittable
{
    public UserResult User { get; set; }
    public bool Commit { get; set; }
}

public class SaveUser : Filter<SaveUserMessage>
{
    public async Task Execute(SaveUserMessage msg)
    {
        msg.OnLog?.Invoke($"{nameof(SaveUser)}");
        
        // Insert query
        msg.Command.CommandText = @"
            INSERT INTO dbo.Users (DisplayName, Permissions, Enabled)
            VALUES (@DisplayName, @Permissions, @Enabled);
            SELECT CAST(SCOPE_IDENTITY() as int);";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@DisplayName", msg.User.DisplayName);
        msg.Command.Parameters.AddWithValue("@Permissions", msg.User.Permissions);
        msg.Command.Parameters.AddWithValue("@Enabled", msg.User.Enabled);
        msg.Result.Id = (int)await msg.Command.ExecuteScalarAsync();
    }
}
```