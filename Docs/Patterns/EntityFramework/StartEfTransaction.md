# StartEfTransaction Filter

To support transactional operations in Entity Framework within a DataPipe pipeline, you can use the ready-made `StartEfTransaction<T>` filter. This filter wraps its child filters in an EF transaction, committing or rolling back based on the message state after execution.

The interface `IUseDbContext` ensures any message type to be used with EF and the `OpenDbContext` filter has a `DbContext` property.

```csharp
    public interface IUseDbContext
    {
        DbContext DbContext { get; set; }
    }
```

## Usage Example

The following example demonstrates how to use the `OpenDbContext` and `StartEfTransaction` filters within a DataPipe pipeline. In this example, an `AppDbContext` is created for each `OrderMessage`, and several filters are executed within the context of that database connection. In order to support transactions, the message type also implements the `IAmCommittable` interface.

```csharp
    pipe.Add(
        new OpenDbContext<OrderMessage>(msg => new AppDbContext(options),
            new StartEfTransaction<OrderMessage>(
                new ProcessOrder(),
                new UpdateInventory(),
                new MarkCommit()  // Sets msg.Commit = true
            )));

    // Message type implementing IUseDbContext and IAmCommittable
    public class OrderMessage : BaseMessage, IUseDbContext, IAmCommittable
    {
        public DbContext DbContext { get; set; } = default!;
        public bool Commit { get; set; } = false;
        // Additional order-related properties
    }`
```

## StartEfTransaction Filter

```csharp
    public class StartEfTransaction<T> : Filter<T>, IAmStructural where T : BaseMessage, IAmCommittable, IUseDbContext
    {
        public bool EmitTelemetryEvent => false; // emit own start event rather than parent so we can capture isolation level

        private readonly IsolationLevel _isolationLevel;
        private readonly Filter<T>[] _filters;

        public StartEfTransaction(params Filter<T>[] filters)
            : this(IsolationLevel.ReadCommitted, filters)
        {
        }

        public StartEfTransaction(IsolationLevel isolationLevel, params Filter<T>[] filters)
        {
            _isolationLevel = isolationLevel;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            var context = msg.DbContext
                ?? throw new InvalidOperationException("DbContext not available on message.");

            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;

            if (context.Database.IsRelational())
            {
                var transaction = await context.Database.BeginTransactionAsync(_isolationLevel, msg.CancellationToken);

                // Build start attributes including any annotations from parent
                var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
                {
                    ["isolation-level"] = _isolationLevel.ToString()
                };
                
                // Clear annotations after consuming them for Start event
                msg.Execution.TelemetryAnnotations.Clear();

                var @txnStart = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(StartEfTransaction<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.Start,
                    MessageId = msg.CorrelationId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Attributes = startAttributes
                };
                if (msg.ShouldEmitTelemetry(@txnStart)) msg.OnTelemetry?.Invoke(@txnStart);

                try
                {
                    await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName);

                    if (msg.Commit)
                    {
                        if (msg.CancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Operation was cancelled before committing transaction.");
                        }

                        if (msg.ShouldStop)
                        {
                            await transaction.RollbackAsync(msg.CancellationToken);
                            msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK ({msg.Execution.Reason})");
                            return;
                        }

                        await transaction.CommitAsync(msg.CancellationToken);
                        msg.OnLog?.Invoke($"INFO: TRANSACTION COMMITTED");
                    }
                    else
                    {
                        structuralReason = "Transaction rolled back (msg.Commit = false)";
                        await transaction.RollbackAsync(msg.CancellationToken);
                        msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK (msg.Commit = false)");
                    }
                }
                catch (Exception ex)
                {
                    structuralOutcome = TelemetryOutcome.Exception;
                    structuralReason = ex.Message;
                    await transaction.RollbackAsync(msg.CancellationToken);
                    msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK (EXCEPTION: {ex.Message})");
                    throw;
                }
                finally
                {
                    structuralSw.Stop();

                    // Build end attributes
                    var endAttributes = new Dictionary<string, object>
                    {
                        ["isolation-level"] = _isolationLevel.ToString(),
                        ["committed"] = msg.Commit && structuralOutcome == TelemetryOutcome.Success
                    };

                    var @txnEnd = new TelemetryEvent
                    {
                        Actor = msg.Actor,
                        Component = nameof(StartEfTransaction<T>),
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
                        Attributes = endAttributes
                    };
                    if (msg.ShouldEmitTelemetry(@txnEnd)) msg.OnTelemetry?.Invoke(@txnEnd);
                    
                    // Clear any remaining annotations to prevent leaking
                    msg.Execution.TelemetryAnnotations.Clear();
                }
            }
            else
            {
                // Non-relational provider: just run the filters without transaction
                msg.OnLog?.Invoke($"INFO: TRANSACTION SKIPPED (non-relational provider)");
                
                await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName);
            }
        }
    }
```