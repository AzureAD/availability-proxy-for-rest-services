// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FUSE.Weld.Base;
using FUSE.Paxos;

namespace RegisterPerfCounters
{
    class Program
    {
        static void Main(string[] args)
        {
            PerformanceCounters.Create(typeof(Counters), typeof(MessageCounters));
        }
    }
}
