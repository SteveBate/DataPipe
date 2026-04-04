# OpenDbContext Filter

In order to work with Entity Framework within a DataPipe pipeline, it is common to open a `DbContext` for the duration of processing a message. The `OpenDbContext` filter pattern encapsulates this behavior, ensuring that a `DbContext` is created, assigned to the message, and properly disposed of after processing. In order to keep the pattern flexible, the filter accepts a factory function to create the `DbContext` based on the message. Telemetry events are emitted for observability to ensure correlation with other pipeline activities but can be omitted if not required via the `TelemetryMode.Off` option on the pipeline.

Use a layered contract approach:

- `IUseDbContext` for structural filters (`OpenDbContext`, `StartEfTransaction`)
- `IUseDbContext<TContext>` for business filters that need typed, no-cast access to tables

This keeps structural filters reusable while still enabling strongly typed EF access where needed.

```csharp
    public interface IUseDbContext
    {
        DbContext DbContext { get; set; }
    }

    public interface IUseDbContext<TContext> : IUseDbContext where TContext : DbContext
    {
        new TContext DbContext { get; set; }
    }
```

## Usage Example

The following example demonstrates how to use the `OpenDbContext` and `StartEfTransaction` filters within a DataPipe pipeline. In this example, an `AppDbContext` is created for each `OrderMessage`, and several filters are executed within the context of that database connection. In order to support transactions, the message type also implements the `IAmCommittable` interface.

```csharp
var pipe = new DataPipe<OrderMessage>();
pipe.Use(new ExceptionAspect<OrderMessage>());
pipe.Add(
    new OpenDbContext<OrderMessage>(msg => new AppDbContext(options),
        new StartEfTransaction<OrderMessage>(
            new ProcessOrder(),
            new UpdateInventory(),
            new MarkCommit()  // Sets msg.Commit = true
        )));

// Message type implementing typed access + base contract for structural filters
public class OrderMessage : BaseMessage, IUseDbContext<AppDbContext>, IAmCommittable
{
    // Strongly typed DbContext property for business filters
    public AppDbContext DbContext { get; set; } = default!;

    // Explicit implementation to satisfy non-generic contract for structural filters
    DbContext IUseDbContext.DbContext
    {
        get => DbContext;
        set => DbContext = (AppDbContext)value;
    }
    public bool Commit { get; set; } = false;
    // Additional order-related properties
}

public sealed class LoadEvents : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        // Typed DbContext access, no cast required
        var pending = await msg.DbContext.Orders
            .Where(o => !o.Delivered)
            .ToListAsync(msg.CancellationToken);

        // ...process pending events
    }
}
```

## Implementation

```csharp
    public class OpenDbContext<T> : Filter<T>, IAmStructural where T : BaseMessage, IUseDbContext
    {
        public bool EmitTelemetryEvent => false;

        private readonly Func<T, DbContext> _factory;
        private readonly Filter<T>[] _filters;

        public OpenDbContext(Func<T, DbContext> factory, params Filter<T>[] filters)
        {
            _factory = factory;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            string databaseName = string.Empty;
            var previousContext = msg.DbContext;

            if (msg.DbContext != null)
            {
                msg.OnLog?.Invoke($"WARNING: DbContext already set on message. Overwriting existing context.");
            }

            await using var context = _factory(msg);
            msg.DbContext = context;
            
            // Try to get database name if possible
            try { databaseName = context.Database.GetDbConnection().Database; } catch {}

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["database"] = databaseName
            };
            
            // Clear annotations after consuming them for Start event
            msg.Execution.TelemetryAnnotations.Clear();

            var @ctxStart = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(OpenDbContext<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@ctxStart)) msg.OnTelemetry?.Invoke(@ctxStart);

            try
            {
                await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName);
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                // Always restore previous context reference so retries/exception paths
                // do not leave msg.DbContext pointing at a disposed instance.
                msg.DbContext = previousContext!;

                structuralSw.Stop();
                
                var @ctxEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(OpenDbContext<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = structuralOutcome,
                    Reason = structuralReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw.ElapsedMilliseconds,
                };
                if (msg.ShouldEmitTelemetry(@ctxEnd)) msg.OnTelemetry?.Invoke(@ctxEnd);
                
                // Clear any remaining annotations to prevent leaking
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
```