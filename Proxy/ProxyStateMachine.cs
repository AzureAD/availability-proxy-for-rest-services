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
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Routing;
using FUSE.Paxos;
using FUSE.Paxos.Azure;
using FUSE.Paxos.Esent;
using FUSE.Weld.Azure;
using FUSE.Weld.Base;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using SignalR.Hubs;
using ReverseProxy.Hubs;
using System.Security.Cryptography.X509Certificates;


namespace ReverseProxy
{
    public class ProxyStateMachine : AdaptiveStateMachine<SerialilzableWebRequest, HttpStatusCode>
    {
        Dictionary<Guid, TaskCompletionSource<HttpWebResponse>> completions = new Dictionary<Guid, TaskCompletionSource<HttpWebResponse>>();
        public Func<SerialilzableWebRequest, Task<HttpWebResponse>> GetResponse { get; set; }
        public string ServiceName { get; private set; }
        CloudStorageAccount cloudStorageAccount;

        public ProxyStateMachine(string self, IDictionary<string, Uri> endpoints, ISubject<Tuple<Uri, Message>> mesh, IStorage<string, SerialilzableWebRequest> storage, string serviceName, IEnumerable<string> preferedLeaders)
            : base(self, endpoints, mesh, storage, preferedLeaders)
        {
            this.ServiceName = serviceName;
            this.cloudStorageAccount = Utility.GetStorageAccount(true);
        }

        public Task<HttpWebResponse> SubmitAsync(SerialilzableWebRequest r)
        {
            var proposal = new Proposal<string, SerialilzableWebRequest>(r);
            var tcs = new TaskCompletionSource<HttpWebResponse>();
            completions.Add(proposal.guid, tcs);
            return this.ReplicateAsync(proposal, CancellationToken.None).ContinueWith(ant => { ant.Wait(); return tcs.Task; }).Unwrap();
        }

        public override Task<HttpStatusCode> ExecuteAsync(int instance, Proposal<string, SerialilzableWebRequest> command)
        {
            return Concurrency.Iterate<HttpStatusCode>(tcs => _executeAsync(tcs, instance, command)); 
        }

        private IEnumerable<Task> _executeAsync(TaskCompletionSource<HttpStatusCode> tcs, int instance, Proposal<string, SerialilzableWebRequest> proposal)
        {
            Action<string> trace = s => FUSE.Paxos.Events.TraceInfo(s, proposal.guid, _paxos.Self, instance, proposal.value.Headers["x-ms-client-request-id"] ?? "");
            
            trace("ExecuteAsync Enter");

            var getResponseTask = Concurrency.RetryOnFaultOrCanceledAsync<HttpWebResponse>(() => GetResponse(proposal.value), t => ShouldRetryGetResponse(t, instance, proposal.guid), 1000);
            yield return getResponseTask;

            trace("ExecuteAsync ResponseReceived");

            if (!proposal.value.Method.Equals("GET", StringComparison.InvariantCultureIgnoreCase))
            {
            var logTask = Concurrency.RetryOnFaultOrCanceledAsync(() => LogResultAsync(instance), _ => true, 1000);
            yield return logTask;

                try
                {
                    logTask.Wait();
            trace("ExecuteAsync Logged");
                }
                catch (Exception e)
                {
                    Validation.TraceException(e, "Exception incrementing LSN");
                }                
            }

            TaskCompletionSource<HttpWebResponse> completion;
            if (completions.TryGetValue(proposal.guid, out completion))
            {
                completion.SetFromTask(getResponseTask);
                completions.Remove(proposal.guid);
            }

            tcs.SetResult(GetStatusCode(getResponseTask));
            

            trace("ExecuteAsync Exit");
        }

        static HttpStatusCode GetStatusCode(Task<HttpWebResponse> task)
        {
            if (task.IsFaulted)
            {
                return Validation.HttpStatusCodes(task.Exception).Single();
            }
            else
            {
                return task.Result.StatusCode;
            }
        }

        static IList<HttpStatusCode> RetryableHttpStatusCodes = new List<HttpStatusCode> {
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout
        };

        private bool ShouldRetryGetResponse(Task<HttpWebResponse> task, int instance, Guid guid)
        {
            if (ShouldRetryGetResponse(task))
            {
                FUSE.Paxos.Events.TraceInfo("ExecuteAsync Retry", guid, _paxos.Self, instance);
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool ShouldRetryGetResponse(Task<HttpWebResponse> task)
        {
            if (task.IsCanceled)
            {
                return true;
            }
            else if (task.IsFaulted)
            {
                var httpStatusCodes = Validation.HttpStatusCodes(task.Exception).ToArray();
                if (httpStatusCodes.Length == 1)
                {
                    var httpStatusCode = httpStatusCodes[0];
                    if (RetryableHttpStatusCodes.Contains(httpStatusCode))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return true;
                }
            }
            else
            {
                throw new ArgumentException("Task must be in faulted or canceled state");
            }

        }

        private Task LogResultAsync(int instance)
        {
            return Task.Factory.Iterate(_logOperation(instance));
        }

        private IEnumerable<Task> _logOperation(int instance)
        {
            var container = cloudStorageAccount.CreateCloudBlobClient().GetContainerReference("root");
            var blob = container.GetBlobReference(ServiceName + "LogPosition");
            while (true)
            {
                using (var task = blob.UploadTextAsync((instance + 1).ToString()))
                {
                    yield return task;
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        break;
                    }
                    else
                    {
                        Trace.Write("LogPositionUpload " + task.Exception.ToString());
                        yield return Task.Factory.StartNewDelayed(30 * 1000);
                        continue;
                    }
                }
            }
        }

        public static ProxyStateMachine New(string location, string serviceName)
        {
            ProxyStateMachine stateMachine;
            var nodeConfig = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".Nodes");
            var nodeDescriptors = nodeConfig.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var nodes = new Dictionary<string, Uri>();
            foreach (var d in nodeDescriptors)
            {
                var n = d.Split('=');
                try
                {
                    nodes.Add(n[0], new UriBuilder() { Host = n[1] }.Uri);
                }
                catch (Exception x)
                {
                    Trace.TraceError(x.ToString());
                }
            }
            Uri host;
            if (nodes.TryGetValue(location, out host))
            {
                var preferedLeaders = RoleEnvironment.GetConfigurationSettingValue(serviceName + ".PreferedActiveMembers").Split(';');
                var localStorage = RoleEnvironment.GetLocalResource("LocalStorage").RootPath;
                var storagePath = Path.Combine(localStorage, serviceName);

                if (!Directory.Exists(storagePath))
                {
                    Directory.CreateDirectory(storagePath);
                }
                var storage = new EsentStorage<string, SerialilzableWebRequest>(storagePath, new Counters(location));
                var configuration = new Configuration<string>(preferedLeaders, preferedLeaders, nodes.Keys);
                storage.TryInitialize(configuration);
                Trace.WriteLine("Local Storage Initialized");

                var meshPath = serviceName + "Mesh.ashx";
                var uri = new UriBuilder(host) { Path = meshPath }.Uri;
                X509Certificate2 cert = Utility.GetCert();
                var mesh = new MeshHandler<Message>(uri, null, cert);
                RouteTable.Routes.Add(new Route(meshPath, new FuncRouteHandler(_ => mesh)));

                FUSE.Weld.Azure.Configuration.SetConfigurationSettingPublisher();
                var s2 = Utility.GetStorageAccount(true);
                var container = s2.CreateCloudBlobClient().GetContainerReference("root");
                container.CreateIfNotExist();

                Trace.Write("Remote Storage Loaded");

                stateMachine = new ProxyStateMachine(location, nodes, mesh, storage, serviceName, preferedLeaders);

                stateMachine.Paxos.WhenDiverged.Subscribe(d =>
                    {
                        Utility.DisableService(stateMachine.ServiceName);
                    });

                Global.lastMessages[serviceName] = new Queue<Timestamped<Tuple<string, Message, string>>>();
                Global.stateMachines[serviceName] = stateMachine;

                var o = stateMachine.Mesh
                    .Where(m => Interesting(m.Item2))
                    .Where(m => stateMachine.EndpointUrisToNames.ContainsKey(m.Item1))
                    .Select(m => Tuple.Create(stateMachine.EndpointUrisToNames[m.Item1], m.Item2, "To"))
                    .Timestamp();

                var i = mesh
                    .Where(m => Interesting(m.Item2))
                    .Where(m => stateMachine.EndpointUrisToNames.ContainsKey(m.Item1))
                    .Select(m => Tuple.Create(stateMachine.EndpointUrisToNames[m.Item1], m.Item2, "From"))
                    .Timestamp();

                i
                    .Merge(o)
                    .Subscribe(m =>
                    {
                        try
                        {
                            lock (Global.lastMessages)
                            {
                                var lastMessages = Global.lastMessages[serviceName];
                                lastMessages.Enqueue(m);
                                while (lastMessages.Count > 1000)
                                {
                                    lastMessages.Dequeue();
                                }
                            }
                        }
                        catch
                        {
                        }
                    });
                var enabled = false;
                var enabledState = container.GetBlobReference(serviceName + "EnabledState.txt");
                try
                {
                    enabled = Boolean.Parse(enabledState.DownloadText());
                }
                catch
                {
                }

                mesh.enabled = enabled;
                return stateMachine;
            }
            else
            {
                throw new ArgumentException("Location not found");
            }
        }


        static string GetConfigurationSettingValueIfPresent(string setting)
        {
            try
            {
                return RoleEnvironment.GetConfigurationSettingValue(setting);
            }
            catch (RoleEnvironmentException)
            {
                return null;
            }
        }

        static bool Interesting(Message m)
        {
            if (m is Message.Gossip)
            {
                return false;
            }

            if (m is Message.Query)
            {
                return false;
            }

            if (m is Message.Initiate<string, SerialilzableWebRequest>)
            {
                return false;
            }
            if (m is Message.RejectionHint<string>)
            {
                return false;
            }

            var p = m as Message.Propose<string, SerialilzableWebRequest>;
            if (p != null)
            {
                return p.proposal is ProposalConfiguration<string>;
            }

            var a = m as Message.Accepted<string, SerialilzableWebRequest>;
            if (a != null)
            {
                return a.proposal is ProposalConfiguration<string>;
            }

            var l = m as Message.Learn<string, SerialilzableWebRequest>;
            if (l != null)
            {
                return l.proposal is ProposalConfiguration<string>;
            }


            return true;
        }

    }
}