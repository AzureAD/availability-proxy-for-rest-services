// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ReverseProxy.Models
{
    public class LogModel
    {
        public int Instance { get; set; }
        public string Message { get; set; }
    }

    public class LiveLogModel
    {
        public string Service { get; set; }
    }
}