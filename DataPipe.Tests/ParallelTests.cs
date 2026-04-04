using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Adapters;
using DataPipe.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DataPipe.Tests
{
    [TestClass]
    public class ParallelTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        private List<ChildMessage> CreateChildren(int count)
        {
            return Enumerable.Range(1, count)
                .Select(i => new ChildMessage { Id = i, Value = 0 })
                .ToList();
        }

        [TestMethod]
        public async Task Should_execute_filters_for_each_child_message()
        {
            // given
            var children = CreateChildren(5);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 1));
        }

        [TestMethod]
        public async Task Should_execute_multiple_filters_per_branch()
        {
            // given
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter(),
                new IncrementChildValueFilter(),
                new IncrementChildValueFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 3));
        }

        [TestMethod]
        public async Task Should_execute_branches_concurrently()
        {
            // given — 10 branches each with a 50ms delay should complete much faster than 500ms sequentially
            var children = CreateChildren(10);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new SlowChildFilter(50)));
            var msg = new TestMessage { Children = children, Service = si };
            var sw = Stopwatch.StartNew();

            // when
            await sut.Invoke(msg);
            sw.Stop();

            // then — should complete in well under 500ms (sequential would be 500ms+)
            Assert.IsTrue(children.All(c => c.Value == 1));
            Assert.IsTrue(sw.ElapsedMilliseconds < 400,
                $"Expected < 400ms (concurrent) but took {sw.ElapsedMilliseconds}ms (sequential would be ~500ms)");
        }

        [TestMethod]
        public async Task Should_copy_infrastructure_properties_to_children()
        {
            // given
            var logMessages = new ConcurrentBag<string>();
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage
            {
                Children = children,
                Service = si,
                Actor = "test-actor",
                OnLog = s => logMessages.Add(s)
            };

            // when
            await sut.Invoke(msg);

            // then — OnLog was called for child filter executions
            Assert.IsTrue(logMessages.Count > 0);
            Assert.IsTrue(logMessages.Any(l => l.Contains("IncrementChildValueFilter")));
        }

        [TestMethod]
        public async Task Should_apply_mapper_to_each_child()
        {
            // given
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                (parent, child) => child.ParentConnectionString = parent.ConnectionString,
                new IncrementChildValueFilter()));
            var msg = new TestMessage
            {
                Children = children,
                ConnectionString = "Server=test;Database=db",
                Service = si
            };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.ParentConnectionString == "Server=test;Database=db"));
            Assert.IsTrue(children.All(c => c.Value == 1));
        }

        [TestMethod]
        public async Task Should_isolate_message_state_between_branches()
        {
            // given — each branch stores its own ID in State then reads it back
            var results = new ConcurrentDictionary<int, Guid>();
            var children = CreateChildren(10);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new LambdaFilter<ChildMessage>(async child =>
                {
                    child.State.Set("myId", child.CorrelationId);
                    await Task.Delay(10); // give time for interleaving
                    var stored = child.State.Get<Guid>("myId");
                    results.TryAdd(child.Id, stored);
                })));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then — each branch saw its own CorrelationId, not another branch's
            foreach (var child in children)
            {
                Assert.IsTrue(results.ContainsKey(child.Id));
                Assert.AreEqual(child.CorrelationId, results[child.Id]);
            }
        }

        [TestMethod]
        public async Task Should_propagate_exception_when_branch_fails_without_try_catch()
        {
            // given
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new FailingChildFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when / then
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await sut.Invoke(msg));
        }

        [TestMethod]
        public async Task Should_isolate_errors_per_branch_with_try_catch()
        {
            // given — odd IDs fail, even IDs succeed
            var children = CreateChildren(6);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new TryCatch<ChildMessage>(
                    tryFilters: [new FailOddChildFilter()],
                    catchFilters: [new LambdaFilter<ChildMessage>(c =>
                    {
                        c.Value = -1; // mark as failed
                        return Task.CompletedTask;
                    })]
                )));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.IsSuccess);
            var even = children.Where(c => c.Id % 2 == 0).ToList();
            var odd = children.Where(c => c.Id % 2 != 0).ToList();
            Assert.IsTrue(even.All(c => c.Value == 1), "Even children should succeed");
            Assert.IsTrue(odd.All(c => c.Value == -1), "Odd children should be marked as failed");
        }

        [TestMethod]
        public async Task Should_respect_cancellation_token()
        {
            // given
            using var cts = new CancellationTokenSource(millisecondsDelay: 30);
            var children = CreateChildren(20);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new SlowChildFilter(5000)));
            var msg = new TestMessage { Children = children, Service = si, CancellationToken = cts.Token };

            // when
            await sut.Invoke(msg);

            // then — not all children should have completed
            var completed = children.Count(c => c.Value == 1);
            Assert.IsTrue(completed < 20, $"Expected < 20 completed but got {completed}");
        }

        [TestMethod]
        public async Task Should_skip_when_selector_returns_null()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => null,
                new IncrementChildValueFilter()));
            sut.Add(new IncrementingNumberFilter()); // should still run
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_skip_when_selector_returns_empty()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Children = [], Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_parallel_completes()
        {
            // given
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Children = children, Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 1));
            Assert.AreEqual(1, msg.Number); // pipeline continued after parallel
        }

        [TestMethod]
        public async Task Should_respect_max_degree_of_parallelism()
        {
            // given — limit to 2 concurrent branches
            var maxConcurrent = 0;
            var currentConcurrent = 0;
            var lockObj = new object();
            var children = CreateChildren(10);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                mapper: null,
                maxDegreeOfParallelism: 2,
                new LambdaFilter<ChildMessage>(async child =>
                {
                    lock (lockObj)
                    {
                        currentConcurrent++;
                        if (currentConcurrent > maxConcurrent)
                            maxConcurrent = currentConcurrent;
                    }
                    await Task.Delay(30);
                    lock (lockObj) { currentConcurrent--; }
                    child.Value = 1;
                })));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 1));
            Assert.IsTrue(maxConcurrent <= 2,
                $"Expected max 2 concurrent but saw {maxConcurrent}");
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_branch_count_and_parallelism()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var children = CreateChildren(5);
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage
            {
                Children = children,
                Service = si,
                OnTelemetry = e => telemetryEvents.Add(e)
            };

            // when
            await sut.Invoke(msg);

            // then
            var startEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "Parallel" && e.Phase == TelemetryPhase.Start);
            Assert.IsNotNull(startEvent);
            Assert.AreEqual(5, startEvent.Attributes["branches"]);
            Assert.AreEqual(-1, startEvent.Attributes["max-parallelism"]);

            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "Parallel" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(5, endEvent.Attributes["branches"]);
        }

        [TestMethod]
        public async Task Should_propagate_telemetry_mode_to_children()
        {
            // given
            var childTelemetryEvents = new ConcurrentBag<TelemetryEvent>();
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage
            {
                Children = children,
                Service = si,
                OnTelemetry = e => childTelemetryEvents.Add(e)
            };

            // when
            await sut.Invoke(msg);

            // then — child filter telemetry events should have been emitted
            var childFilterEvents = childTelemetryEvents
                .Where(e => e.Component == "IncrementChildValueFilter")
                .ToList();
            Assert.IsTrue(childFilterEvents.Count > 0,
                "Expected telemetry events from child filter executions");
        }

        [TestMethod]
        public async Task Should_set_error_status_when_branch_fails_with_exception_aspect()
        {
            // given
            var children = CreateChildren(3);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Parallel<TestMessage, ChildMessage>(
                msg => msg.Children,
                new FailingChildFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(500, msg.StatusCode);
        }
    }
}
