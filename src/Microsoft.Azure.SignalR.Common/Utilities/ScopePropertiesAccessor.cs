// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace Microsoft.Azure.SignalR.Common.Utilities
{
    internal class ScopePropertiesAccessor<TProps>
    {
        // Use async local with indirect reference to TProps to allow for deep cleanup
        private static readonly AsyncLocal<ScopePropertiesAccessor<TProps>> s_currentAccessor = new AsyncLocal<ScopePropertiesAccessor<TProps>>();

        protected internal static ScopePropertiesAccessor<TProps> Current
        {
            get => s_currentAccessor.Value;
            set => s_currentAccessor.Value = value;
        }

        internal TProps Properties { get; set; }
    }

    public class CallScopeId : IDisposable
    {
        private static AsyncLocal<long> CallScope = new AsyncLocal<long>();
        private static long s_callID = 0;
        private long myId = 0;

        public CallScopeId()
        {
            myId = CallScope.Value = Interlocked.Increment(ref s_callID);
        }

        public void Dispose()
        {
            if (myId != Current)
            {
                throw new InvalidCastException($"CallScope.Value has inexplicably changed! expected:{myId} current {Current}");
            }
            CallScope.Value = 0;
        }

        public static long Current => CallScope.Value;
    }
}