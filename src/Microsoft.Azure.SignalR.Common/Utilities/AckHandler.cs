using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR.Common.Utilities;

namespace Microsoft.Azure.SignalR
{
    internal sealed class AckHandler : IDisposable
    {
        public static AckTrace AckTrace = new AckTrace();

        private readonly ConcurrentDictionary<int, AckInfo> _acks = new ConcurrentDictionary<int, AckInfo>();
        private readonly Timer _timer;
        private readonly TimeSpan _ackInterval;
        private readonly TimeSpan _ackTtl;
        private int _currentId = 0;

        public AckHandler(int ackIntervalInMilliseconds = 3000, int ackTtlInMilliseconds = 10000)
        {
            _ackInterval = TimeSpan.FromMilliseconds(ackIntervalInMilliseconds);
            _ackTtl = TimeSpan.FromMilliseconds(ackTtlInMilliseconds);

            bool restoreFlow = false;
            try
            {
                if (!ExecutionContext.IsFlowSuppressed())
                {
                    ExecutionContext.SuppressFlow();
                    restoreFlow = true;
                }

                _timer = new Timer(state => ((AckHandler)state).CheckAcks(), state: this, dueTime: _ackInterval, period: _ackInterval);
            }
            finally
            {
                // Restore the current ExecutionContext
                if (restoreFlow)
                {
                    ExecutionContext.RestoreFlow();
                }
            }
        }

        public Task<AckStatus> CreateAck(out int id, CancellationToken cancellationToken = default)
        {
            id = Interlocked.Increment(ref _currentId);
            var tcs = _acks.GetOrAdd(id, _ => new AckInfo(_ackTtl)).Tcs;
            cancellationToken.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public void TriggerAck(int id, AckStatus ackStatus, string connId, int containerHash)
        {
            bool removed = _acks.TryRemove(id, out var ack);
            bool setResult = false;
            if (removed)
            {
                setResult = ack.Tcs.TrySetResult(ackStatus);
            }
            AckTrace.AddTrace(id, CallScopeId.Current, ClientConnectionScope.ScopeId, $"TriggerAck! Removed: {removed}, SetResult: {setResult}, ackStatus: {ackStatus}, SrvConnId: {connId}, svcContainer# {containerHash}");
        }

        private void CheckAcks()
        {
            var utcNow = DateTime.UtcNow;

            foreach (var pair in _acks)
            {
                if (utcNow > pair.Value.Expired)
                {
                    var tryRemove = _acks.TryRemove(pair.Key, out var ack);
                    AckTrace.AddTrace(pair.Key, CallScopeId.Current, ClientConnectionScope.ScopeId, $"CheckAcks! TryRemove ack with tcs, result: {tryRemove}, tcs.Task status: {ack?.Tcs?.Task?.Status}");

                    if (tryRemove)
                    {
                        Task.Delay(11111).Wait(); //TODO:REMOVE!!
                        ack.Tcs.TrySetResult(AckStatus.Timeout);
                    }
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();

            foreach (var pair in _acks)
            {
                if (_acks.TryRemove(pair.Key, out var ack))
                {
                    ack.Tcs.TrySetCanceled();
                }
            }
        }

        private class AckInfo
        {
            public TaskCompletionSource<AckStatus> Tcs { get; private set; }

            public DateTime Expired { get; private set; }

            public AckInfo(TimeSpan ttl)
            {
                Expired = DateTime.UtcNow.Add(ttl);
                Tcs = new TaskCompletionSource<AckStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}