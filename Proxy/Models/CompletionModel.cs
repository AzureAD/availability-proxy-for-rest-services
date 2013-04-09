// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ReverseProxy.Models
{
    public class CompletionModel
    {
        public int Instance { get; set; }
        public string Message { get; set; }
        public bool Conflict { get; set; }
    }
}