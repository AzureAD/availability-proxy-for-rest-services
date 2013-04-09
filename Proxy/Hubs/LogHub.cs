// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using SignalR.Hubs;
using System.Threading.Tasks;

namespace ReverseProxy.Hubs
{
    [HubName("Log")]
    public class LogHub : Hub
    {
        public Task Join(string service)
        {
            return Groups.Add(Context.ConnectionId, service);
        }
    }
}