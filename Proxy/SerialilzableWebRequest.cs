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
using System.Collections.Specialized;

namespace ReverseProxy
{
    [Serializable]
    public class SerialilzableWebRequest
    {
        public string Path { get; set; }
        public string Query { get; set; }
        public string Method { get; set; }
        public string ContentType { get; set; }
        public byte[] Content { get; set; }
        public NameValueCollection Headers { get; set; }

        public override string ToString()
        {
            return string.Format("{{\n  Path: {0}\n  Method: {1}\n}}", Path, Method);
        }
    }
}