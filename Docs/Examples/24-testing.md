# Testing Guide

This guide covers how to test DataPipe-based code: individual filters in isolation, complete pipelines end-to-end, telemetry output, and lifecycle assertions.

---

## 1. Test a filter in isolation

A filter is a plain class with an `Execute` method. To test it, instantiate it directly and pass a message.

```csharp
[TestClass]
public class ValidateOrderTests
{
    [TestMethod]
    public async Task Should_stop_pipeline_when_order_id_is_missing()
    {
        // given
        var filter = new ValidateOrder();
        var msg = new OrderMessage { OrderId = null };

        // when
        await filter.Execute(msg);

        // then
        Assert.IsTrue(msg.Execution.IsStopped);
        Assert.AreEqual("Order ID is required", msg.StatusMessage);
    }

    [TestMethod]
    public async Task Should_allow_valid_order_through()
    {
        // given
        var filter = new ValidateOrder();
        var msg = new OrderMessage { OrderId = "ORD-001" };

        // when
        await filter.Execute(msg);

        // then
        Assert.IsFalse(msg.Execution.IsStopped);
    }
}
```

No pipeline, no aspects, no setup ceremony. The filter is the unit under test.

## 2. Test a complete pipeline end-to-end

Compose a pipeline exactly as production code would, but use test doubles for external dependencies.

```csharp
[TestClass]
public class OrderProcessingPipelineTests
{
    [TestMethod]
    public async Task Should_process_order_through_entire_pipeline()
    {
        // given
        var sut = new DataPipe<OrderMessage>();
        sut.Use(new ExceptionAspect<OrderMessage>());
        sut.Add(new ValidateOrder());
        sut.Add(new CalculateTotals());
        sut.Add(new ApplyDiscount());

        var msg = new OrderMessage
        {
            OrderId = "ORD-001",
            Lines = [new OrderLine { Qty = 2, UnitPrice = 10.00m }]
        };

        // when
        await sut.Invoke(msg);

        // then
        Assert.IsTrue(msg.IsSuccess);
        Assert.AreEqual(20.00m, msg.Total);
    }
}
```

Always include `ExceptionAspect` in pipeline tests. Without it, a failing filter throws directly instead of setting `msg.StatusCode = 500` and `msg.IsSuccess = false`.

## 3. Assert on message state

After `Invoke`, the message carries all the information you need to verify:

```csharp
// Success assertions
Assert.IsTrue(msg.IsSuccess);
Assert.AreEqual(200, msg.StatusCode);
Assert.IsFalse(msg.Execution.IsStopped);

// Failure assertions
Assert.IsFalse(msg.IsSuccess);
Assert.AreEqual(500, msg.StatusCode);

// Validation-stopped assertions
Assert.IsTrue(msg.Execution.IsStopped);
Assert.AreEqual("Validation failed", msg.StatusMessage);

// Transient state bag
Assert.IsNotNull(msg.State["invoice-id"]);
```

`msg.IsSuccess` is `true` when `StatusCode` is in the 200 range. `Execution.IsStopped` indicates the pipeline was deliberately stopped — not that it failed of its own accord.

## 4. Test lifecycle callbacks

The message provides `OnStart`, `OnSuccess`, `OnError`, and `OnComplete` callbacks. Use them to verify lifecycle events without inspecting internal pipeline state.

```csharp
[TestMethod]
public async Task Should_notify_on_error_when_filter_throws()
{
    // given
    var sut = new DataPipe<OrderMessage>();
    sut.Use(new ExceptionAspect<OrderMessage>());
    sut.Add(new AlwaysFailingFilter());

    Exception captured = null;
    var msg = new OrderMessage
    {
        OnError = (m, ex) => captured = ex
    };

    // when
    await sut.Invoke(msg);

    // then
    Assert.IsNotNull(captured);
    Assert.AreEqual(500, msg.StatusCode);
}

[TestMethod]
public async Task Should_notify_on_success_when_pipeline_completes()
{
    // given
    var sut = new DataPipe<OrderMessage>();
    sut.Use(new ExceptionAspect<OrderMessage>());

    bool succeeded = false;
    var msg = new OrderMessage
    {
        OnSuccess = m => succeeded = true
    };

    // when
    await sut.Invoke(msg);

    // then
    Assert.IsTrue(succeeded);
}
```

## 5. Test telemetry output

Use a `TestTelemetryAdapter` to capture telemetry events in memory and assert on them.

```csharp
[TestMethod]
public async Task Should_emit_pipeline_and_filter_telemetry_events()
{
    // given
    var adapter = new TestTelemetryAdapter();
    var sut = new DataPipe<OrderMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
    sut.Use(new ExceptionAspect<OrderMessage>());
    sut.Use(new TelemetryAspect<OrderMessage>(adapter));
    sut.Add(new ValidateOrder());
    sut.Add(new CalculateTotals());

    var si = new ServiceIdentity
    {
        Name = "OrderService",
        Version = "1.0.0",
        Environment = "Test",
        InstanceId = Guid.NewGuid().ToString()
    };
    var msg = new OrderMessage { OrderId = "ORD-001", Service = si };

    // when
    await sut.Invoke(msg);

    // then
    Assert.IsTrue(adapter.Events.Count > 0);

    var pipelineEnd = adapter.Events.First(e =>
        e.Scope == TelemetryScope.Pipeline &&
        e.Phase == TelemetryPhase.End);
    Assert.IsNotNull(pipelineEnd.DurationMs);

    var filterEvents = adapter.Events.Where(e =>
        e.Scope == TelemetryScope.Filter).ToList();
    Assert.IsTrue(filterEvents.Count >= 2); // at least one per filter
}
```

The `TestTelemetryAdapter` accepts an optional `ITelemetryPolicy` to mirror production filtering:

```csharp
var policy = new ExcludeStartEventsPolicy(excludeFilterStartEvents: true);
var adapter = new TestTelemetryAdapter(policy);
```

Remember: `ServiceIdentity` must be set on the message when `TelemetryMode` is not `Off`.

## 6. Test telemetry policies

Policies control which events reach the adapter. Test them directly.

```csharp
[TestMethod]
public void ExcludeStartEventsPolicy_should_exclude_filter_start_events()
{
    // given
    var policy = new ExcludeStartEventsPolicy(excludeFilterStartEvents: true);
    var filterStart = new TelemetryEvent
    {
        Scope = TelemetryScope.Filter,
        Phase = TelemetryPhase.Start
    };

    // when / then
    Assert.IsFalse(policy.ShouldInclude(filterStart));
}

[TestMethod]
public void ExcludeStartEventsPolicy_should_always_include_pipeline_events()
{
    // given
    var policy = new ExcludeStartEventsPolicy(excludeFilterStartEvents: true);
    var pipelineStart = new TelemetryEvent
    {
        Scope = TelemetryScope.Pipeline,
        Phase = TelemetryPhase.Start
    };

    // when / then
    Assert.IsTrue(policy.ShouldInclude(pipelineStart));
}
```

## 7. Test structural filters

Structural filters like `OnCircuitBreak`, `OnTimeoutRetry`, and `OnRateLimit` are tested the same way: wrap a known filter, invoke, and assert on message state.

### Circuit breaker

```csharp
[TestMethod]
public async Task Should_trip_circuit_after_threshold_reached()
{
    // given
    var state = new CircuitBreakerState();
    for (int i = 0; i < 5; i++)
    {
        var pipe = new DataPipe<TestMessage>();
        pipe.Use(new ExceptionAspect<TestMessage>());
        pipe.Add(new OnCircuitBreak<TestMessage>(state,
            failureThreshold: 5,
            filters: [new AlwaysFailingFilter()]));
        await pipe.Invoke(new TestMessage { Service = si });
    }

    // then
    Assert.AreEqual(CircuitState.Open, state.Status);
}
```

Key point: `CircuitBreakerState` is shared across pipeline instances. In tests, create one state object and reuse it across multiple pipeline invocations to observe state transitions.

### Rate limiter

```csharp
[TestMethod]
public async Task Should_delay_when_bucket_is_empty()
{
    // given
    var state = new RateLimiterState();
    var sut = new DataPipe<TestMessage>();
    sut.Use(new ExceptionAspect<TestMessage>());
    sut.Add(new OnRateLimit<TestMessage>(state,
        capacity: 1,
        leakInterval: TimeSpan.FromSeconds(10),
        behavior: RateLimitExceededBehavior.Delay,
        filters: [new IncrementingNumberFilter()]));

    // Consume the single token
    await sut.Invoke(new TestMessage { Number = 0, Service = si });

    // when — second request should be delayed
    var sw = Stopwatch.StartNew();
    var msg = new TestMessage { Number = 0, Service = si };
    await sut.Invoke(msg);
    sw.Stop();

    // then — filter still executed (Delay mode waits, doesn't reject)
    Assert.AreEqual(1, msg.Number);
}
```

### Retry with recovery

```csharp
[TestMethod]
public async Task Should_recover_after_transient_failure()
{
    // given
    var sut = new DataPipe<TestMessage>();
    sut.Use(new ExceptionAspect<TestMessage>());
    sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries: 3,
        retryWhen: null,
        customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10),
        new FailNTimesThenSucceedFilter(failCount: 2)));

    var msg = new TestMessage { Service = si };

    // when
    await sut.Invoke(msg);

    // then
    Assert.IsTrue(msg.IsSuccess);
    Assert.AreEqual(3, msg.Attempt); // 1 original + 2 retries
}
```

## 8. Test branching logic

```csharp
[TestMethod]
public async Task Policy_should_route_to_correct_branch()
{
    // given
    var sut = new DataPipe<OrderMessage>();
    sut.Use(new ExceptionAspect<OrderMessage>());
    sut.Add(new Policy<OrderMessage>(msg =>
        msg.IsHighValue
            ? new PriorityProcessing()
            : new StandardProcessing()));

    var msg = new OrderMessage { IsHighValue = true };

    // when
    await sut.Invoke(msg);

    // then
    Assert.AreEqual("priority", msg.ProcessingPath);
}
```

## 9. Test cancellation

```csharp
[TestMethod]
public async Task Should_stop_pipeline_when_cancellation_requested()
{
    // given
    var cts = new CancellationTokenSource();
    cts.Cancel();

    var sut = new DataPipe<OrderMessage>();
    sut.Use(new ExceptionAspect<OrderMessage>());
    sut.Add(new SlowFilter(delayMs: 5000));

    var msg = new OrderMessage { CancellationToken = cts.Token };

    // when
    await sut.Invoke(msg);

    // then - pipeline did not complete the slow filter
    Assert.IsFalse(msg.IsSuccess);
}
```

## 10. Recommended test message pattern

Define a test message that implements the contracts you need:

```csharp
class TestMessage : BaseMessage, IAmCommittable, IAmRetryable
{
    // IAmRetryable
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; }

    // IAmCommittable
    public bool Commit { get; set; } = true;

    // Test-specific properties
    public int Number { get; set; }
    public string[] Words { get; set; } = [];
    public List<ChildMessage> Children { get; set; } = [];
    public ConcurrentBag<int> Results { get; } = [];
}
```

Only implement the interfaces your test needs. For simple filter tests, `BaseMessage` alone is sufficient. Add `IAmCommittable` when testing transaction behavior and `IAmRetryable` when testing retry logic.

## 11. Recommended test filter patterns

```csharp
// A filter that always fails — for testing error paths
class AlwaysFailingFilter : Filter<TestMessage>
{
    public Task Execute(TestMessage msg) =>
        throw new InvalidOperationException("Simulated failure");
}

// A filter that fails N times then succeeds — for testing retry recovery
class FailNTimesThenSucceedFilter : Filter<TestMessage>
{
    private int _callCount;
    private readonly int _failCount;

    public FailNTimesThenSucceedFilter(int failCount) => _failCount = failCount;

    public Task Execute(TestMessage msg)
    {
        _callCount++;
        if (_callCount <= _failCount)
            throw new InvalidOperationException($"Failure {_callCount}");
        msg.Number = _callCount;
        return Task.CompletedTask;
    }
}

// A filter that takes time — for testing timeouts and cancellation
class SlowFilter : Filter<TestMessage>
{
    private readonly int _delayMs;
    public SlowFilter(int delayMs) => _delayMs = delayMs;

    public async Task Execute(TestMessage msg)
    {
        await Task.Delay(_delayMs, msg.CancellationToken);
        msg.Number += 1;
    }
}
```

## 12. Test naming convention

Use `Should_expected_behavior_when_condition` naming:

```
Should_stop_pipeline_when_order_id_is_missing
Should_trip_circuit_after_threshold_reached  
Should_retry_and_recover_after_transient_failure
Should_emit_pipeline_and_filter_telemetry_events
```

Structure every test with given/when/then comments:

```csharp
[TestMethod]
public async Task Should_increment_number_when_filter_executes()
{
    // given
    var filter = new IncrementingNumberFilter();
    var msg = new TestMessage { Number = 0 };

    // when
    await filter.Execute(msg);

    // then
    Assert.AreEqual(1, msg.Number);
}
```

## What this demonstrates

- Filters are testable in complete isolation — no framework setup required
- Pipelines are testable end-to-end by composing real or mock filters
- Message state (`IsSuccess`, `StatusCode`, `Execution.IsStopped`, `State`) carries all assertions
- Telemetry is testable in-memory via `TestTelemetryAdapter`
- Structural filters (`OnCircuitBreak`, `OnRateLimit`, `OnTimeoutRetry`) are tested by observing shared state objects
- Lifecycle callbacks (`OnStart`, `OnSuccess`, `OnError`, `OnComplete`) provide hooks for event verification
