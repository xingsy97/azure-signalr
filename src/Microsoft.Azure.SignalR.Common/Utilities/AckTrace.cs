// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Microsoft.Azure.SignalR.Common.Utilities
{
    public class AckTrace
    {
        class RingElement
        {
            public int? _ackId;
            public long? _callId;
            public long? _connId;
            public string _syncCtx;
            public string _trace;
        }

        const int RingSize = 10000;
        long _current = 0;
        RingElement[] _ring = new RingElement[RingSize];
        ConcurrentDictionary<int, List<string>> _dict = new ConcurrentDictionary<int, List<string>>();

        public void AddTrace(int? ackId, long? callId, long? connId, string trace)
        {
            long index = Interlocked.Increment(ref _current) % RingSize;
            _ring[index] = new RingElement() { _ackId = ackId, _callId = callId, _connId = connId, _syncCtx = SynchronizationContext.Current?.GetType()?.Name, _trace = trace };
        }

        public string FindTraces(int ackId)
        {
            var sb = new StringBuilder();

            try
            {
                return FindTraceImpl(ackId, sb);
            }
            catch (Exception e)
            {
                sb.AppendLine("- - - - - -");
                sb.AppendLine(e.ToString());
                return sb.ToString();
            }
        }

        private string FindTraceImpl(int ackId, StringBuilder sb)
        { 
            // find relevant call id
            // (should be only 1 but given that we don't know what happens here we count all that got mixed with the same ackId)
            var callList = new HashSet<long?>();
            foreach (var e in _ring)
            {
                if (e != null && e._ackId == ackId)
                {
                    if (e._callId.HasValue && e._callId != 0)
                    {
                        callList.Add(e._callId);
                    }
                }
            }

            var extraConnList = new HashSet<(long index, long? connId)>();
            var start = _current + 1;
            sb.AppendLine($"Finding traces for ackId {ackId} with {callList.Count} relevant calls");

            for (long i = start; i < start + RingSize; i++)
            {
                var index = i % RingSize;
                var e = _ring[index]; 
                // log all acks associated with the callId (multiple EPs)
                if (e != null)
                {
                    if (e._ackId == ackId || callList.Contains(e._callId))
                    {
                        sb.AppendLine($"ack: {e._ackId}, \t call: {e._callId}, \t conn: {e._connId}, \t ctx: {e._syncCtx}, \t trace: {e._trace}");
                        sb.AppendLine();

                        // see if any extra connections got mixed in
                        if (e._connId != null && e._connId != 0 && !extraConnList.Any(x=>x.connId == e._connId))
                        {
                            extraConnList.Add((index, e._connId));
                        }
                    }
                }
            }

            // what else? the main suspect is an extra connection so we try to chase it out
            if (extraConnList.Count > 1)
            {
                sb.AppendLine("\r\n ========================== extra connections got mixed in ===============================");
                // dump calls and acks that got mixed in (normally should be none)
                foreach (var exConn in extraConnList)
                {
                    sb.AppendLine($"main or extra connection {exConn.connId}"); // todo only print extra
                    long? prevCallId = null;

                    // dump the logs for the preceding callId from the same connId
                    for (long i = exConn.index + RingSize; i > exConn.index; i--)
                    {
                        // stop at the first call id not from already known calls
                        var testCallId = _ring[i % RingSize]._callId;
                        if (testCallId != null && !callList.Contains(testCallId))
                        {
                            prevCallId = testCallId;
                            break;
                        }
                    }

                    if (prevCallId != null)
                    {
                        // quick version of dumping everything we know about it (in the correct order)
                        for (long i = exConn.index; i < exConn.index + RingSize; i++)
                        {
                            var e = _ring[i % RingSize];
                            if (e?._callId == prevCallId)
                            {
                                sb.AppendLine($"ack: {e._ackId}, \t call: {e._callId}, \t conn: {e._connId}, \t ctx: {e._syncCtx}, \t trace: {e._trace}");
                                sb.AppendLine();
                            }
                        }
                    }
                }
            }

            return sb.ToString();
        }
    }
}
