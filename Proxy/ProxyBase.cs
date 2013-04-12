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
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Routing;
using FUSE.Paxos;
using FUSE.Paxos.Azure;
using FUSE.Paxos.Esent;
using FUSE.Weld.Base;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;

namespace ReverseProxy
{
    public abstract class ProxyBase : HttpAsyncHandlerBase
    {
        public override Task ProcessRequestAsync(HttpContext context)
        {
            return Task.Factory.Iterate(_processRequestAsync(context)).ContinueWith(a => { if (a.Status != TaskStatus.RanToCompletion && a.Exception != null) { Trace.TraceError(a.Exception.ToString()); } a.Wait(); });
        }

        private IEnumerable<Task> _processRequestAsync(HttpContext context)
        {
            if (!Enabled || Lagging)
            {
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                context.Response.Write(Lagging ? "Service Unavailable: Lagging" : "Service Unavailable: Not Enabled");
                context.Response.End();
                yield break;
            }
            else if (_authenticate(context))
            {
                Task<SerialilzableWebRequest> request = MakeSerialilzableWebRequest(context.Request);
                yield return request;

                Task<HttpWebResponse> response = GetResponse(request.Result);
                yield return response;

                var task = ProcessResponse(context.Response, response, request.Result.Path);
                yield return task;

                task.Wait();
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.End();
                yield break;
            }
        }

        private Task ProcessResponse(HttpResponse client, Task<HttpWebResponse> service, string path)
        {
            return Task.Factory.Iterate(_processResponse(client, service, path));
        }

        private IEnumerable<Task> _processResponse(HttpResponse clientResponse, Task<HttpWebResponse> remoteCall, string path)
        {
            HttpWebResponse remoteResponse;

            if (remoteCall.Status == TaskStatus.RanToCompletion)
            {
                remoteResponse = remoteCall.Result;
            }
            else
            {
                var we = (WebException)Validation.EnumerateInner(remoteCall.Exception).Where(e => e is WebException).FirstOrDefault();
                if (we != null && we.Response != null)
                {
                    remoteResponse = (HttpWebResponse)we.Response;
                }
                else
                {
                    clientResponse.StatusCode = 500;
                    clientResponse.End();
                    yield break;
                }
            }

            clientResponse.TrySkipIisCustomErrors = true;
            clientResponse.StatusCode = (int)remoteResponse.StatusCode;
            clientResponse.ContentType = remoteResponse.ContentType;
            clientResponse.Headers.AddUnrestricted(remoteResponse.Headers);

            if (ShouldRewrite(remoteResponse.ContentType))
            {

                Encoding responseEncoding;
                string responseContent;
                using (var ms = new MemoryStream())
                {
                    var st = Streams.CopyStreamAsync(remoteResponse.GetResponseStream(), ms);
                    yield return st;
                    st.Wait();

                    if (remoteResponse.CharacterSet == "")
                    {
                        responseEncoding = Encoding.UTF8;
                    }
                    else
                    {
                        try
                        {
                            // CharacterSet may be wrong case etc.  May need to clean up.
                            responseEncoding = Encoding.GetEncoding(remoteResponse.CharacterSet);
                        }
                        catch
                        {
                            responseEncoding = Encoding.UTF8;
                        }
                    }

                    responseContent = responseEncoding.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                }

                responseContent = RewriteResponse(responseContent, path);

                var rb = responseEncoding.GetBytes(responseContent);
                var task = System.IO.StreamExtensions.WriteAsync(clientResponse.OutputStream, rb, 0, rb.Length);
                yield return task;
                task.Wait();
            }
            else
            {
                var task = Streams.CopyStreamAsync(remoteResponse.GetResponseStream(), clientResponse.OutputStream);
                yield return task;
                task.Wait();
            }
        }

        protected abstract bool ShouldRewrite(string contentType);

        protected abstract string RewriteResponse(string responseContent, string path);

        public virtual Task<HttpWebResponse> GetResponse(SerialilzableWebRequest request)
        {
            return Concurrency.Iterate<HttpWebResponse>(r => _getResponse(request, r));
        }

        protected virtual bool Lagging { get { return false; } }

        protected virtual bool Enabled { get { return true; } }

        static readonly IList<string> noContent = new List<string> { "GET", "HEAD", "DELETE" };

        private bool HasNoContent(string method)
        {
            return
                method.Equals("get", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("head", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("delete", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<Task> _getResponse(SerialilzableWebRequest request, TaskCompletionSource<HttpWebResponse> result)
        {
            //Trace.TraceInformation("ReverseProxy._getResponse Enter");
            Uri uri = BuildUri(request);
            HttpWebRequest remoteRequest;
            remoteRequest = (HttpWebRequest)HttpWebRequest.Create(uri);
            remoteRequest.Method = request.Method;
            remoteRequest.ContentType = request.ContentType;
            remoteRequest.Headers.AddUnrestricted(request.Headers);
            remoteRequest.ContentLength = request.Content.Length;
            AugmentHeaders(request, remoteRequest);

            if (!HasNoContent(request.Method))
            {
                //Trace.TraceInformation("ReverseProxy._getResponse Has Content");
                using (var stream = remoteRequest.GetRequestStream())
                using (var task = Streams.WriteAsync(stream, request.Content, 0, request.Content.Length))
                {
                    yield return task;
                    task.Wait();
                }
            }

            //Trace.TraceInformation("ReverseProxy._getResponse Calling GetResponseAsync");
            using (var task = remoteRequest.GetResponseAsync(TimeSpan.FromSeconds(15)))
            {
                yield return task;
                //Trace.TraceInformation("ReverseProxy._getResponse GetResponseAsync Returned");
                switch (task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        result.SetResult((HttpWebResponse)task.Result);
                        yield break;

                    case TaskStatus.Canceled:
                        result.SetCanceled();
                        yield break;

                    case TaskStatus.Faulted:
                        result.SetException(task.Exception.InnerExceptions);
                        yield break;
                }
                throw new InvalidOperationException("The task was not completed.");

                //Trace.TraceInformation("ReverseProxy._getResponse result set");
            }
        }

        protected virtual void AugmentHeaders(SerialilzableWebRequest request, HttpWebRequest remoteRequest)
        {
        }

        protected abstract Uri BuildUri(SerialilzableWebRequest request);

        private Task<SerialilzableWebRequest> MakeSerialilzableWebRequest(HttpRequest httpRequest)
        {
            return Concurrency.Iterate<SerialilzableWebRequest>(r => _makeSerializableWebRequest(httpRequest, r));
        }

        private IEnumerable<Task> _makeSerializableWebRequest(HttpRequest request, TaskCompletionSource<SerialilzableWebRequest> result)
        {
            string path = ExtractPath(request.Url.AbsolutePath);
            var remote = new SerialilzableWebRequest
            {
                Path = path,
                Query = request.Url.Query.TrimStart('?'),
                Method = request.HttpMethod,
                ContentType = request.ContentType,
                Headers = request.Headers
            };
            remote.Content = new byte[request.InputStream.Length];
            using (var stream = new MemoryStream(remote.Content, 0, remote.Content.Length, true, true))
            {
                var task = Streams.CopyStreamAsync(request.InputStream, stream);
                yield return task;
                task.Wait();
            }
            result.SetResult(remote);
        }

        protected abstract string ExtractPath(string path);

        protected abstract bool _authenticate(HttpContext context);

    }
}