using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Policies;
using System;
using System.Collections.Generic;

namespace DataPipe.Tests.Support
{
    public sealed class TestTelemetryAdapter : ITelemetryAdapter
    {
        private readonly ITelemetryPolicy _policy;
        private List<TelemetryEvent> _events = new();
        public List<TelemetryEvent> Events { get; set; } = [];

        public TestTelemetryAdapter(ITelemetryPolicy policy = null)
        {
            _policy = policy ?? new DefaultCaptureEverythingPolicy();
        }

        public void Flush()
        {
            if (_events.Count == 0) return;
            Events = [.. _events];
        }

        public void Handle(TelemetryEvent evt)
        {
            if (!_policy.ShouldInclude(evt)) return;
            _events.Add(evt);
        }
    }
}
