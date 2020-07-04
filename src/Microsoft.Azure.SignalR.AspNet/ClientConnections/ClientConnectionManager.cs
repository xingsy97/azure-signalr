﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Owin;

namespace Microsoft.Azure.SignalR.AspNet
{
    internal class ClientConnectionManager : IClientConnectionManager
    {
        private readonly HubConfiguration _configuration;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, ClientConnectionContext> _clientConnections = new ConcurrentDictionary<string, ClientConnectionContext>();

        public ClientConnectionManager(HubConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration;
            _logger = loggerFactory?.CreateLogger<ClientConnectionManager>() ?? NullLogger<ClientConnectionManager>.Instance;
        }

        public async Task<IServiceTransport> CreateConnection(OpenConnectionMessage message)
        {
            var dispatcher = new ClientConnectionHubDispatcher(_configuration, message.ConnectionId);
            dispatcher.Initialize(_configuration.Resolver);

            var responseStream = new MemoryStream();
            var hostContext = GetHostContext(message, responseStream);

            if (dispatcher.Authorize(hostContext.Request))
            {
                // ProcessRequest checks if the connectionToken matches "{connectionid}:{userName}" format with context.User
                await dispatcher.ProcessRequest(hostContext);

                // TODO: check for errors written to the response
                if (hostContext.Response.StatusCode != 200)
                {
                    Log.ProcessRequestError(_logger, message.ConnectionId, hostContext.Request.QueryString.ToString());
                    var errorResponse = GetContentAndDispose(responseStream);
                    throw new InvalidOperationException(errorResponse);
                }

                return (AzureTransport)hostContext.Environment[AspNetConstants.Context.AzureSignalRTransportKey];
            }

            // This happens when hub is not found
            throw new InvalidOperationException("Unable to authorize request");
        }

        public bool TryAddClientConnection(ClientConnectionContext connection)
        {
            return _clientConnections.TryAdd(connection.ConnectionId, connection);
        }

        public bool TryRemoveClientConnection(string connectionId, out ClientConnectionContext connection)
        {
            return _clientConnections.TryRemove(connectionId, out connection);
        }

        public bool TryGetClientConnection(string connectionId, out ClientConnectionContext connection)
        {
            return _clientConnections.TryGetValue(connectionId, out connection);
        }

        public IReadOnlyDictionary<string, ClientConnectionContext> ClientConnections => _clientConnections;

        internal HostContext GetHostContext(OpenConnectionMessage message, Stream responseStream)
        {
            var connectionId = message.ConnectionId;
            var context = new OwinContext();
            var response = context.Response;
            var request = context.Request;

            response.Body = responseStream;

            var user = request.User = message.GetUserPrincipal();

            request.Path = new PathString("/");

            string queryString = message.QueryString;
            if (queryString.Length > 0)
            {
                // The one from Azure SignalR always contains a leading '?' character however the Owin one does not
                if (queryString[0] == '?')
                {
                    queryString = queryString.Substring(1);
                }

                request.QueryString = new QueryString(queryString);
            }

            if (message.Headers != null)
            {
                foreach (var pair in message.Headers)
                {
                    request.Headers.Add(pair.Key, pair.Value);
                }
            }
            return new HostContext(context.Environment);
        }

        internal static string GetContentAndDispose(MemoryStream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        public Task WhenAllCompleted() => Task.CompletedTask;


        private sealed class ClientConnectionHubDispatcher : HubDispatcher
        {
            private readonly string _connectionId;

            public ClientConnectionHubDispatcher(HubConfiguration config, string connectionId) : base(config)
            {
                _connectionId = connectionId;
            }

            protected override bool TryGetConnectionId(HostContext context, string connectionToken, out string connectionId, out string message, out int statusCode)
            {
                connectionId = _connectionId;
                message = null;
                statusCode = 200;
                return true;
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, string, Exception> _processRequestError =
                LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1, "ProcessRequestError"), "ProcessRequest for {connectionId} fails with {queryString} ");

            public static void ProcessRequestError(ILogger logger, string connectionId, string queryString)
            {
                _processRequestError(logger, connectionId, queryString, null);
            }
        }
    }
}
