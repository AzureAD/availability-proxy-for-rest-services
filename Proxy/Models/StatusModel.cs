// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ReverseProxy.Models
{
    public class StatusModel
    {
        public string Location { get; set; }
        public TimeSpan LastContact { get; set; }
        public string Color { get; set; }
    }
}