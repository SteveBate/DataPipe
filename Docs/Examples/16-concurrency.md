# Concurrency and Parallel Execution

DataPipe is fully async and safe to use concurrently.

Pipelines are stateless, and messages are isolated per invocation, which makes it natural to process multiple messages in parallel using standard .NET constructs such as:

- Task.WhenAll
- Parallel.ForEachAsync
- background workers
- message consumers

DataPipe deliberately does not manage thread scheduling, degree-of-parallelism, or work distribution. Those concerns are left to the host application, which is best placed to make those decisions.

This keeps DataPipe simple, predictable, and composable while still enabling high-throughput, parallel workloads when used appropriately.

**DataPipe scales by processing more messages, not by making individual pipelines more complex.**

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