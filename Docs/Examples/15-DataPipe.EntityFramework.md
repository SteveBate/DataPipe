# Entity Framework

The DataPipe.EntityFramework package provides ready-made filters for integrating DataPipe pipelines with Entity Framework Core. These filters help manage database transactions and ensure data consistency during pipeline execution.

### Load user example

```csharp
var options = new DbContextOptionsBuilder<UsersDbContext>()
    .UseSqlServer("Data Source=...")
    .Options;
        
var pipe = new DataPipe<LoadUserMessage>();
pipe.Use(new ExceptionAspect<LoadUserMessage>());
pipe.Add(
    new OpenDbContext<LoadUserMessage>((m) => new UsersDbContext(options),
        new LoadUser()));

var msg = new LoadUserMessage { Id = 1, OnLog = Console.WriteLine, OnError = (e, m) => Console.WriteLine(e) };
await pipe.Invoke(msg);
Console.WriteLine(msg.Result.DisplayName);
```

### Save user example

```csharp
var options = new DbContextOptionsBuilder<UsersDbContext>()
    .UseSqlServer("Data Source=...")
    .Options;
        
var pipe = new DataPipe<SaveUserMessage>();
pipe.Use(new ExceptionAspect<SaveUserMessage>());
pipe.Add(
    new StartEfTransaction<SaveUserMessage>(
        new OpenDbContext<SaveUserMessage>((m) => new UsersDbContext(options),
            new SaveUser())));

var user = new User { DisplayName = "Joe Bloggs", Permissions = 4, Enabled = true };
var msg = new SaveUserMessage { Commit = true, User = user, OnLog = Console.WriteLine, OnError = (e, m) => Console.WriteLine(e) };
await pipe.Invoke(msg);
Console.WriteLine("User saved with ID: " + msg.Result.Id);
```

### The building blocks

- `OpenDbContext<TMessage>`: Opens an EF Core `DbContext` for the duration of the filter(s). The context is available via `msg.DbContext`.
- `StartEfTransaction<TMessage>`: Starts an EF Core transaction. Commits if the message completes successfully, rolls back on error or if `msg.Commit` is false.
- `ExceptionAspect<TMessage>`: Catches exceptions thrown by downstream filters, allowing for centralized error handling and logging.
- Custom filters (e.g., `LoadUser`, `SaveUser`): Implement your business logic using the provided `DbContext` and transaction management.

These components work together to simplify database operations within your DataPipe pipelines, ensuring that data integrity is maintained through proper transaction handling.

### Result implementation

```csharp
public class CommonResult 
{
	[NotMapped] public bool Success { get; set; } = true;
	[NotMapped] public string Message { get; set; } = string.Empty;
	[NotMapped] public int StatusCode { get; set; } = 200;
}

[Table("Users", Schema = "dbo")]
public class UserResult : CommonResult
{
    public int Id { get; set; }
    public string DisplayName { get; set; }
    public int Permissions { get; set; }
    public bool Enabled { get; set; }
}
```

### EF DbContext

```csharp
// a typical DbContext for EF
public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }
    public DbSet<UserResult> Users { get; set; }
}
```

### Our custom message base

```csharp
// our custom BaseMessage that implements IUseDbContext for EF filters, and has a strongly typed Result property
public class AppContext<TResult> : BaseMessage, IUseDbContext where TResult : CommonResult, new()
{
    public DbContext DbContext { get; set; }
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
        
        msg.Result = await ((UsersDbContext)msg.DbContext).Users.FindAsync(msg.Id);
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
        
        var db = (UsersDbContext)msg.DbContext;
        db.Users.Add(msg.User);
        await db.SaveChangesAsync();
        msg.Result = msg.User; // return the saved user with new generated ID
    }
}
```