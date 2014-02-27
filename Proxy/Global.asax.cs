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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Threading;
using System.Web;
using FUSE.Paxos;
using FUSE.Weld.Azure;
using FUSE.Weld.Base;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using System.Web.Routing;
using System.Web.Mvc;
using System.Web.Optimization;
using ReverseProxy;

namespace ReverseProxy
{
    public class Global : HttpApplication
    {
        //public static string cloudDriveLetter;
        //public static RPStateMachine stateMachine;
        public static bool initialized = false;
        public static IDictionary<string, AdaptiveStateMachine<SerialilzableWebRequest, HttpStatusCode>> stateMachines = new Dictionary<string, AdaptiveStateMachine<SerialilzableWebRequest, HttpStatusCode>>();
        public static Dictionary<string, Queue<Timestamped<Tuple<string, Message, string>>>> lastMessages = new Dictionary<string, Queue<Timestamped<Tuple<string, Message, string>>>>();
        static HashSet<string> Initalized = new HashSet<string>();
        static Mutex beginApplication = new Mutex();

        protected void Application_Start(object sender, EventArgs e)
        {
            AreaRegistration.RegisterAllAreas();

            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            BundleMobileConfig.RegisterBundles(BundleTable.Bundles);

            beginApplication.WaitOne();

            try
            {
                ServicePointManager.DefaultConnectionLimit = Int32.MaxValue;
                ServicePointManager.UseNagleAlgorithm = false;
                ServicePointManager.Expect100Continue = false;

                Validation.SwallowUnobservedTaskExceptions();
                Validation.TraceAppDomainUnhandledExceptions();

                foreach (var service in RoleEnvironment.GetConfigurationSettingValue("Services").Split(';'))
                {
                    var nk = service.Split('=');
                    var name = nk[0];
                    var kind = nk[1];
                    if (!Global.Initalized.Contains(name))
                    {
                        InitializeProxy(name, kind);
                        Global.Initalized.Add(name);
                    }
                }
            }
            finally
            {
                beginApplication.ReleaseMutex();
            }
        }

        static void InitializeProxy(string service, string kind)
        {
            var location = RoleEnvironment.GetConfigurationSettingValue("Name");

            try
            {
                var SM = ProxyStateMachine.New(location, service);
                var RP = ProxyFactory.New(kind, SM);

                FUSE.Weld.Azure.Configuration.SetConfigurationSettingPublisher();
                var s2 = Utility.GetStorageAccount(true);
                var container = s2.CreateCloudBlobClient().GetContainerReference("root");
                container.CreateIfNotExist();
                var logPosition = container.GetBlobReference(service + "LogPosition");

                var startAt = 0;
                try
                {
                    startAt = Int32.Parse(logPosition.DownloadText());
                }
                catch
                {
                }

                SM.StartAsync(startAt);
            }
            catch (ArgumentException ex)
            {
                if (!ex.Message.Contains("Location"))
                {
                    throw;
                }
            }
        }

        protected void Session_Start(object sender, EventArgs e)
        {

        }

        static Mutex beginRequest = new Mutex();
        static bool initalized = false;
        protected void Application_BeginRequest(object sender, EventArgs e)
        {

            beginRequest.WaitOne();

            try
            {
                if (!initalized)
                {
                    Configuration.SetAzureTraceListener();
                    var counterSpecifiers = PerformanceCounters.CounterSpecifiers(typeof(Counters)).Concat(PerformanceCounters.CounterSpecifiers(typeof(MessageCounters)));
                    Trace.TraceError("Initializing Azure Diagnostics");
                    Configuration.InitializeAzureDiagnostics(counterSpecifiers);
                    initalized = true;
                }
            }
            catch (Exception)
            {
                var location = Global.stateMachines.First().Value.Paxos.Self;
                Trace.TraceError("Error in Application_BeginRequest at " + location);
                Thread.Sleep(180000);
            }
            finally
            {
                beginRequest.ReleaseMutex();
            }
        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {

        }

        protected void Application_Error(object sender, EventArgs e)
        {

        }

        protected void Session_End(object sender, EventArgs e)
        {

        }

        protected void Application_End(object sender, EventArgs e)
        {
            //stateMachine.Dispose();
        }
    }
}
