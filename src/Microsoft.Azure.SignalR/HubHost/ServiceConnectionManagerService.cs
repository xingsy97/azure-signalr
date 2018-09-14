// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.SignalR
{
    internal class ServiceConnectionManagerService : IHostedService
    {
        private readonly ServiceConnectionManagerServiceOptions _options;
        public ServiceConnectionManagerService(IOptions<ServiceConnectionManagerServiceOptions> options)
        {
            _options = options.Value;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var connection in _options.Connections)
            {
                tasks.Add(connection.StartAsync());
            }
            return Task.WhenAll(tasks);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var connection in _options.Connections)
            {
                tasks.Add(connection.StopAsync());
            }
            return Task.WhenAll(tasks);
        }
    }

    internal class ServiceConnectionManagerServiceOptions
    {
        public List<IServiceConnectionManager> Connections { get; set; } = new List<IServiceConnectionManager>();
    }
}