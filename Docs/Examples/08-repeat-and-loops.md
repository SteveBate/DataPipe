# Repeat and RepeatUntil — Loops

DataPipe provides two loop filters: `Repeat<T>` for open-ended loops controlled by `msg.Execution.Stop()`, and `RepeatUntil<T>` for loops that terminate when a predicate returns `true`.

## Repeat — loop until stopped

`Repeat<T>` executes child filters in a loop until a filter calls `msg.Execution.Stop()`:

```csharp
var pipeline = new DataPipe<QueueMessage>();

pipeline.Add(new Repeat<QueueMessage>(
    new DequeueNextItem(),
    new IfTrue<QueueMessage>(msg => msg.QueueEmpty,
        new LambdaFilter<QueueMessage>(msg =>
        {
            msg.Execution.Stop();
            return Task.CompletedTask;
        })),
    new ProcessItem()
));

await pipeline.Invoke(msg);
```

After the loop exits, `Execution.Reset()` is called automatically so downstream filters are not affected by the stopped state.

## RepeatUntil — loop until condition is met

`RepeatUntil<T>` provides a cleaner alternative when the exit condition can be expressed as a predicate:

```csharp
var pipeline = new DataPipe<BatchMessage>();

pipeline.Add(new RepeatUntil<BatchMessage>(msg => msg.AllBatchesProcessed,
    new FetchNextBatch(),
    new TransformBatch(),
    new SaveBatch()
));

await pipeline.Invoke(msg);
```

The predicate is evaluated before the first iteration. If it returns `true` immediately, no filters execute.

## Throttled loops with DelayExecution

When calling external APIs in a loop, the pipeline may execute faster than the target system can handle. Use `DelayExecution<T>` to throttle the loop:

### Rate-limited API polling

```csharp
var pipeline = new DataPipe<SyncMessage>();

pipeline.Add(new RepeatUntil<SyncMessage>(msg => msg.AllPagesFetched,
    new DelayExecution<SyncMessage>(TimeSpan.FromMilliseconds(500)),
    new FetchNextPage(),
    new ProcessPageResults()
));

await pipeline.Invoke(msg);
```

A 500ms delay before each iteration keeps the request rate at roughly 2 calls per second.

### ETL with third-party rate limits

```csharp
var pipeline = new DataPipe<ImportMessage>();

pipeline.Add(new Repeat<ImportMessage>(
    new GetNextRecord(),
    new IfTrue<ImportMessage>(msg => msg.NoMoreRecords,
        new LambdaFilter<ImportMessage>(msg =>
        {
            msg.Execution.Stop();
            return Task.CompletedTask;
        })),
    new DelayExecution<ImportMessage>(TimeSpan.FromSeconds(1),
        new PostToThirdPartyApi()),
    new MarkRecordSynced()
));

await pipeline.Invoke(msg);
```

Here `DelayExecution` wraps only the API call — a 1-second pause before each post. The record fetch and marking steps run at full speed.

### Delay as standalone throttle

`DelayExecution` can also be used standalone (without wrapping child filters) as a simple pause between loop steps:

```csharp
pipeline.Add(new Repeat<PollingMessage>(
    new CheckForUpdates(),
    new IfTrue<PollingMessage>(msg => msg.HasUpdates,
        new ProcessUpdates()),
    new IfTrue<PollingMessage>(msg => msg.ShouldStop,
        new LambdaFilter<PollingMessage>(msg =>
        {
            msg.Execution.Stop();
            return Task.CompletedTask;
        })),
    new DelayExecution<PollingMessage>(TimeSpan.FromSeconds(5))
));
```

A 5-second pause at the end of each loop iteration creates a polling interval without blocking the thread.

## Loop with resilience

Loops compose naturally with retry and circuit breaker for robust ETL pipelines:

```csharp
var pipeline = new DataPipe<EtlMessage>();

pipeline.Add(new RepeatUntil<EtlMessage>(
    msg => msg.AllRecordsProcessed,
    new FetchNextBatch(),
    new DelayExecution<EtlMessage>(TimeSpan.FromMilliseconds(200)),
    new OnTimeoutRetry<EtlMessage>(maxRetries: 2,
        new Timeout<EtlMessage>(TimeSpan.FromSeconds(10),
            new PushToExternalSystem())),
    new UpdateProgress()
));

await pipeline.Invoke(msg);
```

Each iteration: fetch a batch, pause 200ms, push with a 10-second timeout and up to 2 retries, then update progress.
