// Copyright (c) Microsoft Corporation
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