//-------------------------------------------------------------------------------------------------
// <copyright company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
// EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR 
// CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing 
// permissions and limitations under the License.
// </copyright>
//
// <summary>
// 
//
//     
// </summary>
//-------------------------------------------------------------------------------------------------
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