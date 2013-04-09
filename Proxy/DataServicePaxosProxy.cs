// Copyright (c) Microsoft Corporation
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Routing;
using FUSE.Weld.Base;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace ReverseProxy
{
    class DataServicePaxosProxy : DataServiceProxy
    {
        ProxyStateMachine stateMachine;

        DataServicePaxosProxy(string resourceManaged, string resourcePrestented, ProxyStateMachine stateMachine)
            : base(resourceManaged, resourcePrestented, stateMachine.ServiceName)
        {
            var serviceName = stateMachine.ServiceName;
            this.stateMachine = stateMachine;
            this.stateMachine.GetResponse = base.GetResponse;

            RouteTable.Routes.Add(new Route(serviceName + "/{*path}", new FuncRouteHandler(_ => this)));
        }

        public static DataServicePaxosProxy New(ProxyStateMachine stateMachine)
        {
            var resourcePresented = RoleEnvironment.GetConfigurationSettingValue(stateMachine.ServiceName + ".ResourcePresented");
            var resourceManaged = RoleEnvironment.GetConfigurationSettingValue(stateMachine.ServiceName + ".ResourceManaged");
            return new DataServicePaxosProxy(resourceManaged, resourcePresented, stateMachine);
        }

        public override Task<HttpWebResponse> GetResponse(SerialilzableWebRequest request)
        {
            if (request.Method.Equals("get", StringComparison.OrdinalIgnoreCase) || request.Method.Equals("head", StringComparison.OrdinalIgnoreCase))
            {
                return base.GetResponse(request);
            }
            else
            {
                return stateMachine.SubmitAsync(request);
            }
        }
    }
}