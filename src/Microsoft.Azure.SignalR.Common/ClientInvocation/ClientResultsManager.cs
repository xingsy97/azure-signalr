﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Azure.SignalR
{
    internal class ClientResultsManager : IClientResultsManager, IInvocationBinder
    {
        private readonly ConcurrentDictionary<string, PendingInvocation> _pendingInvocations = new();
        private readonly ConcurrentDictionary<string, List<ServiceMappingMessage>> _serviceMappingMessages = new();
#if NETCOREAPP5_0_OR_GREATER
        private ulong _lastInvocationId = 0;
#else
        private long _lastInvocationId = 0;     // Interlocked.Increment(value) does not support ulong when .NET version < 5.0
#endif

        private readonly IHubProtocolResolver _hubProtocolResolver;

        public ClientResultsManager(IHubProtocolResolver hubProtocolResolver)
        {
            _hubProtocolResolver = hubProtocolResolver;
        }

        public string GetNewInvocationId(string connectionId, string serverGUID)
        {
            return $"{connectionId}-{serverGUID}-{Interlocked.Increment(ref _lastInvocationId)}";
        }

        public Task<T> AddInvocation<T>(string connectionId, string invocationId, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSourceWithCancellation<T>(
                cancellationToken,
                () => TryCompleteResult(connectionId, CompletionMessage.WithError(invocationId, "Canceled")));

            var result = _pendingInvocations.TryAdd(invocationId, new PendingInvocation(typeof(T), connectionId, tcs, static (state, completionMessage) =>
            {
                var tcs = (TaskCompletionSourceWithCancellation<T>)state;
                if (completionMessage.HasResult)
                {
                    tcs.SetResult((T)completionMessage.Result);
                }
                else
                {
                    tcs.SetException(new Exception(completionMessage.Error));
                }
            }
            ));
            Debug.Assert(result);

            tcs.RegisterCancellation();

            return tcs.Task;
        }

        public void AddServiceMappingMessage(string invocationId, ServiceMappingMessage serviceMappingMessage)
        {
            if (_serviceMappingMessages.ContainsKey(serviceMappingMessage.InstanceId))
            {
                _serviceMappingMessages[serviceMappingMessage.InstanceId].Add(serviceMappingMessage);
            }
            else
            {
                _serviceMappingMessages[serviceMappingMessage.InstanceId] = new List<ServiceMappingMessage> { serviceMappingMessage };
            }
        }

        public bool TryRemoveInvocation(string invocationId, out PendingInvocation invocation)
        {
            return _pendingInvocations.TryRemove(invocationId, out invocation);
        }

        public void CleanupInvocations(string instanceId)
        {
            foreach (var serviceMappingMessage in _serviceMappingMessages[instanceId])
            {
                if (_pendingInvocations.TryRemove(serviceMappingMessage.InvocationId, out var item))
                {
                    var message = new CompletionMessage(serviceMappingMessage.InvocationId, $"Connection '{serviceMappingMessage.ConnectionId}' disconnected.", null, false);
                    item.Complete(item.Tcs, message);
                }
            }
        }

        public bool TryCompleteResult(string connectionId, CompletionMessage message)
        {
            if (_pendingInvocations.TryGetValue(message.InvocationId!, out var item))
            {
                if (item.ConnectionId != connectionId)
                {
                    throw new InvalidOperationException($"Connection ID '{connectionId}' is not valid for invocation ID '{message.InvocationId}'.");
                }

                // if false the connection disconnected right after the above TryGetValue
                // or someone else completed the invocation (likely a bad client)
                // we'll ignore both cases
                if (_pendingInvocations.TryRemove(message.InvocationId!, out _))
                {
                    item.Complete(item.Tcs, message);
                    return true;
                }
                return false;
            }
            else
            {
                // connection was disconnected or someone else completed the invocation
                return false;
            }
        }

        public bool TryCompleteResultFromSerializedMessage(string connectionId, string protocol, ReadOnlySequence<byte> message)
        {
            var proto = _hubProtocolResolver.GetProtocol(protocol, new string[] { protocol });
            if (proto == null)
            {
                throw new InvalidOperationException($"Not supported protcol {protocol} by server");
            }

            if (proto.TryParseMessage(ref message, this, out var message1))
            {
                return TryCompleteResult(connectionId, message1 as CompletionMessage);
            }
            return false;
        }

        // Implemented for interface IInvocationBinder
        public Type GetReturnType(string invocationId)
        {
            if (TryGetInvocationReturnType(invocationId, out var type))
            {
                return type;
            }
            throw new InvalidOperationException($"Invocation ID '{invocationId}' is not associated with a pending client result.");
        }

        public bool TryGetInvocationReturnType(string invocationId, out Type type)
        {
            if (_pendingInvocations.TryGetValue(invocationId, out var item))
            {
                type = item.Type;
                return true;
            }
            type = null;
            return false;
        }

        // Unused, here to honor the IInvocationBinder interface but should never be called
        public IReadOnlyList<Type> GetParameterTypes(string methodName)
        {
            throw new NotImplementedException();
        }

        // Unused, here to honor the IInvocationBinder interface but should never be called
        public Type GetStreamItemType(string streamId)
        {
            throw new NotImplementedException();
        }
    }
}