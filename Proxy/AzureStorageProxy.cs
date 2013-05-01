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
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Web.Routing;
using FUSE.Weld.Base;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Linq;
using FUSE.Paxos.Azure;
using FUSE.Paxos;

namespace ReverseProxy
{
    public class AzureStorageProxy : AzureStorageProxyBase
    {
        ProxyStateMachine stateMachine;

        AzureStorageProxy(string resourcePresented, ProxyStateMachine stateMachine)
            : base(resourcePresented, stateMachine.ServiceName)
        {
            var serviceName = stateMachine.ServiceName;
            this.stateMachine = stateMachine;
            this.stateMachine.GetResponse = base.GetResponse;
            this.ManagedAccount = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".StorageAccount");
            this.ManagedKey = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".StorageKey");
            this.PresentedAccount = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".PresentedAccount");
            this.PresentedKey = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".PresentedKey");
            this.prefix = serviceName;
            this.stateMachine = stateMachine;

            RouteTable.Routes.Add(new Route(serviceName + "/{*path}", new FuncRouteHandler(_ => this)));
        }

        public static AzureStorageProxy New(ProxyStateMachine stateMachine)
        {
            var resource = RoleEnvironment.GetConfigurationSettingValue(stateMachine.ServiceName + ".ResourcePresented");
            return new AzureStorageProxy(resource, stateMachine);
        }
            
        protected override bool Lagging 
        { 
            get
            {
                return stateMachine.Paxos.Storage.Constrained.Maximum - stateMachine.CountExecuted > stateMachine.Paxos.Policy.Phase2ConcurrencyLimit;
            }
        }            

        public override Task<HttpWebResponse> GetResponse(SerialilzableWebRequest request)
        {
            //Trace.TraceInformation("AzureStorageProxy.GetResponse");
            bool read = request.Method.Equals("get",StringComparison.OrdinalIgnoreCase) || request.Method.Equals("head", StringComparison.OrdinalIgnoreCase);
            var cacheHeader = request.Headers["Cache-Control"];
            var noCache = cacheHeader != null && cacheHeader.Equals("no-cache", StringComparison.InvariantCultureIgnoreCase);
            var cacheParam = request.Query.Contains("$nocache=true");
            noCache |= cacheParam;
            if (read && !noCache)
            {
                return base.GetResponse(request);
            }
            else
            {
                return stateMachine.SubmitAsync(request);
            }
        }

        protected override bool _authenticate(HttpContext context)
        {
            var request = context.Request;
            var p = request.Url.AbsolutePath.Split('/');
            var service = p[2];

            if (request.HttpMethod.Equals("delete", StringComparison.OrdinalIgnoreCase) &&
                service.Equals("queue", StringComparison.OrdinalIgnoreCase) &&
                request.Url.AbsolutePath.IndexOf("/messages/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            var leaseAction = request.Headers.Get("x-ms-lease-action");
            if (leaseAction != null)
            {
                return false;
            }
            
            return base._authenticate(context);
        }

        protected override void AugmentHeaders(SerialilzableWebRequest request, HttpWebRequest remoteRequest)
        {
            // This should not be neccessary, but one server occasionally throws on the set and this fixes it.
            request.Headers = new System.Collections.Specialized.NameValueCollection(request.Headers);

            var if_match = request.Headers.Get("If-Match");
            if (if_match != null)
            {
                remoteRequest.Headers.Set("If-Match", "*");
                request.Headers.Set("If-Match", "*");
            }
            if (request.Headers.AllKeys.ContainsInvariantIgnoreCase("Date"))
            {
                request.Headers.Remove("Date");
            }
            var now = DateTime.UtcNow.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            request.Headers.Set("x-ms-date", now);
            remoteRequest.Headers.Set("x-ms-date", now);
            base.AugmentHeaders(request, remoteRequest);
        }

        protected override bool Enabled
        {
            get
            {
                var mesh = (MeshHandler<Message>)stateMachine.Mesh;                 
                return mesh.enabled;
            }
        }
    }
}