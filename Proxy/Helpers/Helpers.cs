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
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure;
using FUSE.Paxos.Azure;
using FUSE.Paxos;
using System.Security.Cryptography.X509Certificates;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Net;
using FUSE.Weld.Base;

namespace ReverseProxy
{
    public static class Utility
    {
        public static Task<WebResponse> GetResponseAsync(this WebRequest webRequest, TimeSpan timeout)
        {
            return Concurrency.Iterate<WebResponse>(tcs => _getResponseAsync(tcs, webRequest, timeout));
        }

        static IEnumerable<Task> _getResponseAsync(TaskCompletionSource<WebResponse> tcs, WebRequest request, TimeSpan timeout)
        {
            using (var cancellation_token = new Concurrency.TimeoutToken(timeout))
            using (var registration_token = cancellation_token.Token.Register(() => { request.Abort(); }))
            {
                using (var task_get_response = request.GetResponseAsync())
                {
                    yield return task_get_response;
                    tcs.SetFromTask(task_get_response);
                    yield break;
                }
            }
        }

        public static X509Certificate2 GetCert()
        {
            var thumbprint = RoleEnvironment.GetConfigurationSettingValue("MeshMutualAuthThumbprint");
            X509Certificate2 cert = null;
            if (!String.IsNullOrEmpty(thumbprint))
            {
                var store = new X509Store(StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                if (certs.Count == 0)
                {
                    throw new KeyNotFoundException("The thumbprint is not present in the local machine store");
                }
                else
                {
                    cert = certs[0];
                }
            }
            return cert;
        }


        public static StorageCredentialsAccountAndKey GetStorageCredentials()
        {
            var account = RoleEnvironment.GetConfigurationSettingValue("StorageAccountName");
            var key = RoleEnvironment.GetConfigurationSettingValue("StorageAccountKey");
            return new StorageCredentialsAccountAndKey(account, key);
        }

        public static CloudStorageAccount GetStorageAccount(bool useHttps = false)
        {
            return new CloudStorageAccount(GetStorageCredentials(), useHttps);
        }

        public static CloudBlob GetBlobReference(string name)
        {
            var container = GetStorageAccount().CreateCloudBlobClient().GetContainerReference("root");
            return container.GetBlobReference(name);
        }

        public static void EnableService(string service)
        {
            ((MeshHandler<Message>)Global.stateMachines[service].Mesh).enabled = true;
            Utility.GetBlobReference(service + "EnabledState.txt").UploadText("true");
            //SetEndpointPolidy(service, true);
        }

        public static void DisableService(string service)
        {
            ((MeshHandler<Message>)Global.stateMachines[service].Mesh).enabled = false;
            Utility.GetBlobReference(service + "EnabledState.txt").UploadText("false");
            //SetEndpointPolicy(service, false);
        }
    }
}