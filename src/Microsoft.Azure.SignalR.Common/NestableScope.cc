// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.SignalR.Common.Utilities;
using System;

namespace Microsoft.Azure.SignalR.Common
{
    /// <summary>
    /// Represents a generic nestable scope for flowing properties along with execution context.
    /// </summary>
    internal class NestableScope<TProps> : IDisposable
        where TProps : class
    {
        private ScopePropertiesAccessor<TProps> _previousScope;
        private ScopePropertiesAccessor<TProps> _thisScope;

        public NestableScope(TProps properties)
        {
            _previousScope = ScopePropertiesAccessor<TProps>.Current;
            _thisScope = ScopePropertiesAccessor<TProps>.Current = new ScopePropertiesAccessor<TProps>() { Properties = properties };
        }

        /// <summary>
        /// provides access to this scope properties 
        /// </summary>
        public TProps Properties => _thisScope.Properties;

        /// <summary>
        /// provides access to the current execution context scope's properties
        /// </summary>
        public static TProps CurrentScopeProperties => ScopePropertiesAccessor<TProps>.Current?.Properties;

        /// <summary>
        /// performs 'deep' cleanup of the current scope context and restores the previous one
        /// </summary>
        public void Dispose()
        {
            // Cleanup references to the properties within the current scope before it gets replaced with the previous one
            // This ensures that all unawaited tasks created within this scope will not leak references to TNestableProps
            ScopePropertiesAccessor<TProps>.Current.Properties = default;
            ScopePropertiesAccessor<TProps>.Current = _previousScope;
        }
    }

    public class TraceProperties
    {
        public bool _trace;
        string _scope;
        public TraceProperties(bool b, string s) { _trace = b; _scope = s; }
    }

    public class TracingScopeV1 : IDisposable
    {
        NestableScope<TraceProperties> _nestableScope;
        private ClientConnectionScopeInternal _serviceConnectionScope;

        public TracingScopeV1(TraceProperties trace) 
        {
            _nestableScope = new NestableScope<TraceProperties>(trace);
            _serviceConnectionScope = new ClientConnectionScopeInternal();
        }

        public TraceProperties Properties => _nestableScope.Properties;
        public static TraceProperties CurrentScopeProperties => NestableScope<TraceProperties>.CurrentScopeProperties;

        public void Dispose()
        {
            _nestableScope.Dispose();
            _serviceConnectionScope.Dispose();
        }
    }

    public class TracingScopeV2 : IDisposable
    {
        private ScopePropertiesAccessor<TraceProperties> _previousScope;
        private ScopePropertiesAccessor<TraceProperties> _thisScope;
        private ClientConnectionScopeInternal _serviceConnectionScope;


        public TracingScopeV2(TraceProperties properties)
        {
            _serviceConnectionScope = new ClientConnectionScopeInternal();

            _previousScope = ScopePropertiesAccessor<TraceProperties>.Current;
            _thisScope = ScopePropertiesAccessor<TraceProperties>.Current = new ScopePropertiesAccessor<TraceProperties>() { Properties = properties };
        }

        public TraceProperties Properties => _thisScope.Properties;
        public static TraceProperties CurrentScopeProperties => ScopePropertiesAccessor<TraceProperties>.Current?.Properties;

        public void Dispose()
        {
            _serviceConnectionScope.Dispose();
        }
    }
}
