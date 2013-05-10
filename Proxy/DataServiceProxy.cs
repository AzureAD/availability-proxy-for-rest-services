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
using System.Linq;
using System.Web;

namespace ReverseProxy
{
    public class DataServiceProxy : ProxyBase
    {
        public string ResourceManaged { get; private set; }
        public string ResourcePresented { get; private set; }
        public string ServiceName { get; private set; }

        public DataServiceProxy(string ResourceManaged, string ResourcePresented, string ServiceName)
        {
            this.ResourceManaged = ResourceManaged;
            this.ResourcePresented = ResourcePresented;
            this.ServiceName = ServiceName;
        }

        protected override bool ShouldRewrite(string contentType)
        {
            return true;
        }

        protected override string RewriteResponse(string responseContent, string path)
        {
            return responseContent.Replace(ResourceManaged, ResourcePresented + "/" + ServiceName);
        }

        protected override Uri BuildUri(SerialilzableWebRequest request)
        {
            var ub = new UriBuilder(ResourceManaged);
            ub.Path = ub.Path + "/" + request.Path;
            ub.Query = request.Query;
            return ub.Uri;
        }

        protected override string ExtractPath(string path)
        {
            var p = path.Split('/');
            return String.Join(",", p.Skip(2));
        }

        protected override bool _authenticate(HttpContext context)
        {
            return true;
        }
    }
}