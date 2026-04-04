# Concurrency — Multiple Pipeline Invocations

DataPipe is fully async and safe to use concurrently.

Pipelines are stateless, and messages are isolated per invocation, which makes it natural to process multiple messages in parallel using standard .NET constructs such as:

- Task.WhenAll
- Parallel.ForEachAsync
- background workers
- message consumers

This is the simplest form of parallelism with DataPipe: the host application drives concurrency by invoking the same pipeline with different messages simultaneously. No special configuration is needed — pipelines are thread-safe by design.

```csharp
var pipeline = BuildHtmlScrapePipeline();
var messages = [
    new HtmlScrapeMessage { Url = "https://example.com/page1" },
    new HtmlScrapeMessage { Url = "https://example.com/page2" },
    new HtmlScrapeMessage { Url = "https://example.com/page3" },
    // more messages
];

await Parallel.ForEachAsync(messages, async (msg, ct) =>
{
    await pipeline.Invoke(msg);
});

// or

await Task.WhenAll(messages.Select(msg => pipeline.Invoke(msg)));
```

## When to use external concurrency vs the Parallel filter

Use **external concurrency** (this pattern) when the messages arrive independently — from an API controller, a queue consumer, or a batch of work items that all use the same pipeline.

Use the **`Parallel<TParent, TChild>` filter** when a single pipeline needs to fan out over a collection of child messages mid-execution — for example, processing all order lines concurrently after loading the order. See the [Parallel](21-parallel.md) example for details.