// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.SignalR.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.SignalR
{
    internal class ServiceConnectionManager<THub> : IServiceConnectionManager<THub>, IConnectionFactory where THub : Hub
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ServiceConnectionManager<THub>> _logger;
        private readonly ServiceOptions _options;
        private readonly IServiceEndpointProvider _serviceEndpointProvider;
        private readonly IClientConnectionManager _clientConnectionManager;
        private readonly IServiceProtocol _serviceProtocol;
        private readonly IServiceProvider _serviceProvider;
        private readonly IClientConnectionFactory _clientConnectionFactory;
        private readonly string _userId;
        private readonly List<ServiceConnection> _serviceConnections = new List<ServiceConnection>();
        private static readonly string Name = $"ServiceHubDispatcher<{typeof(THub).FullName}>";
        private static Dictionary<string, string> CustomHeader = new Dictionary<string, string> { { "User-Agent", ProductInfo.GetProductInfo() } };

        public ServiceConnectionManager(IServiceProtocol serviceProtocol,
                                        IClientConnectionManager clientConnectionManager,
                                        IServiceEndpointProvider serviceEndpointProvider,
                                        IOptions<ServiceOptions> options,
                                        ILoggerFactory loggerFactory,
                                        IClientConnectionFactory clientConnectionFactory,
                                        IServiceProvider serviceProvider)
        {
            _serviceProtocol = serviceProtocol;
            _clientConnectionManager = clientConnectionManager;
            _serviceEndpointProvider = serviceEndpointProvider;
            _options = options != null ? options.Value : throw new ArgumentNullException(nameof(options));

            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<ServiceConnectionManager<THub>>();
            _clientConnectionFactory = clientConnectionFactory;
            _userId = GenerateServerName();
            _serviceProvider = serviceProvider;
        }

        private static string GenerateServerName()
        {
            // Use the machine name for convenient diagnostics, but add a guid to make it unique.
            // Example: MyServerName_02db60e5fab243b890a847fa5c4dcb29
            return $"{Environment.MachineName}_{Guid.NewGuid():N}";
        }

        private Uri GetServiceUrl(string connectionId)
        {
            var baseUri = new UriBuilder(_serviceEndpointProvider.GetServerEndpoint<THub>());
            var query = "cid=" + connectionId;
            if (baseUri.Query != null && baseUri.Query.Length > 1)
            {
                baseUri.Query = baseUri.Query.Substring(1) + "&" + query;
            }
            else
            {
                baseUri.Query = query;
            }
            return baseUri.Uri;
        }

        public async Task<ConnectionContext> ConnectAsync(TransferFormat transferFormat, string connectionId, CancellationToken cancellationToken = default)
        {
            var httpConnectionOptions = new HttpConnectionOptions
            {
                Url = GetServiceUrl(connectionId),
                AccessTokenProvider = () => Task.FromResult(_serviceEndpointProvider.GenerateServerAccessToken<THub>(_userId)),
                Transports = HttpTransportType.WebSockets,
                SkipNegotiation = true,
                Headers = CustomHeader
            };
            var httpConnection = new HttpConnection(httpConnectionOptions, _loggerFactory);
            try
            {
                await httpConnection.StartAsync(transferFormat);
                return httpConnection;
            }
            catch
            {
                await httpConnection.DisposeAsync();
                throw;
            }
        }

        public Task DisposeAsync(ConnectionContext connection)
        {
            return ((HttpConnection)connection).DisposeAsync();
        }

        public async Task StartAsync()
        {
            var connectionDelegate = new ConnectionBuilder(_serviceProvider).UseHub<THub>().Build();

            for (var i = 0; i < _options.ConnectionCount; i++)
            {
                var serviceConnection = new ServiceConnection(_serviceProtocol, _clientConnectionManager, this, _loggerFactory, connectionDelegate, _clientConnectionFactory, Guid.NewGuid().ToString());
                _serviceConnections.Add(serviceConnection);
            }
            
            Log.StartingConnection(_logger, Name, _options.ConnectionCount);

            var tasks = _serviceConnections.Select(c => c.StartAsync());
            await Task.WhenAll(tasks);
        }

        public async Task StopAsync()
        {
            Log.StartingConnection(_logger, Name, _options.ConnectionCount);

            var tasks = _serviceConnections.Select(c => c.StopAsync());
            await Task.WhenAll(tasks);
        }

        public async Task WriteAsync(ServiceMessage serviceMessage)
        {
            var index = StaticRandom.Next(_serviceConnections.Count);
            await _serviceConnections[index].WriteAsync(serviceMessage);
        }

        public async Task WriteAsync(string partitionKey, ServiceMessage serviceMessage)
        {
            if (_serviceConnections.Count == 0) return;

            // If we hit this check, it is a code bug.
            if (string.IsNullOrEmpty(partitionKey))
            {
                throw new ArgumentNullException(nameof(partitionKey));
            }

            var index = (partitionKey.GetHashCode() & int.MaxValue) % _serviceConnections.Count;
            await _serviceConnections[index].WriteAsync(serviceMessage);
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, int, Exception> _startingConnection =
                LoggerMessage.Define<string, int>(LogLevel.Debug, new EventId(1, "StartingConnection"), "Staring {name} with {connectionNumber} connections...");

            public static void StartingConnection(ILogger logger, string name, int connectionNumber)
            {
                _startingConnection(logger, name, connectionNumber, null);
            }
        }
    }
}
