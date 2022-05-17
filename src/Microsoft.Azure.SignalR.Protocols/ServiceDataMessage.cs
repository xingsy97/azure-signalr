// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.SignalR.Protocol
{
    /// <summary>
    /// Base class for data messages from Azure SignalR Service to server.
    /// </summary>
    public abstract class ServiceDataMessage : ExtensibleServiceMessage, IMessageWithTracingId
    {
        public ulong? TracingId { get; set; }
        // Information might be in service side though wrap by server. So current server may not know whom to send.
        // public string ServerId { get; set; }
    }

    public class ServiceCompletionMessage : ServiceDataMessage
    {
        public ServiceCompletionMessage(string connectionId, string invocationId, string callerId, ReadOnlyMemory<byte> payload, string error = null, ulong? tracingId = null)
        {
            ConnectionId = connectionId;
            InvocationId = invocationId;
            CallerId = callerId;
            Payload = payload;
            Error = error;
        }

        public string InvocationId { get; set; }
        public string ConnectionId { get; set; }
        public string CallerId { get; set; }
        public string Error { get; set; }
        public ReadOnlyMemory<byte> Payload { get; set; }
    }
}
