// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ReverseProxy
{
    public class ProxyFactory
    {
        public static ProxyBase New(string kind, ProxyStateMachine stateMachine)
        {
            if (kind.Equals("AzureStorage", StringComparison.InvariantCultureIgnoreCase))
            {
                return AzureStorageProxy.New(stateMachine);
            }

            if (kind.Equals("DataService", StringComparison.InvariantCultureIgnoreCase))
            {
                return DataServicePaxosProxy.New(stateMachine);
            }

            throw new ArgumentOutOfRangeException(kind + " is not a known service type");
        }
    }
}