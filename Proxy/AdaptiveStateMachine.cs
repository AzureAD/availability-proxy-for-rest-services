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
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Paxos;
using FUSE.Weld.Base;

namespace ReverseProxy
{
    public abstract class AdaptiveStateMachine<T,R> : StateMachine<string,T,R>
    {
        public readonly Dictionary<string, DateTimeOffset> LastContactByNode = new Dictionary<string, DateTimeOffset>();

        public readonly ISubject<Tuple<Uri, Message>> Mesh;

        private readonly ISubject<Tuple<string, Message>, Tuple<string, Message>> _outgoing;

        IEnumerable<string> preferredLeaders;
        Timer configurationTimer;
        public IDictionary<string, Uri> EndpointNamesToUris;
        public IDictionary<Uri, string> EndpointUrisToNames;

        public IList<Tuple<DateTimeOffset, string>> Events = new List<Tuple<DateTimeOffset, string>>();

        public IEnumerable<string> PreferredLeaders { get { return preferredLeaders; } }

        TimeSpan configurationTimeout = TimeSpan.FromSeconds(10);
        TimeSpan NodeStale = TimeSpan.FromSeconds(30);

        public DateTimeOffset LastContact(string t)
        {
            if (t == _paxos.Self)
            {
                return _paxos.Scheduler.Now;
            }

            lock (LastContactByNode)
            {
               
                DateTimeOffset lc;
                if (LastContactByNode.TryGetValue(t, out lc))
                {
                    return lc;
                }
                else
                {
                    return DateTimeOffset.MinValue;
                }
            }
        }

        int CheckCount = 0;
        void CheckConfiguration()
        {
            //Trace.WriteLine("In CheckConfiguration");
            try
            {
                var checkCount = Interlocked.Increment(ref CheckCount);
                if (checkCount == 1)
                {


                    if (_paxos.LeaderHint == _paxos.Self)
                    {

                        var c = _paxos.ConfigurationHint;

                        IEnumerable<string> activeNodes;


                        activeNodes = c.Proposers.Where(t => _paxos.Scheduler.Now - LastContact(t) < NodeStale);
                        Trace.WriteLine("Active Nodes: " + string.Join(", ", activeNodes));
                        var goodPrefered = preferredLeaders.Where(t => _paxos.Scheduler.Now - LastContact(t) < NodeStale);
                        var preferedMissing = goodPrefered.Except(activeNodes).Take(1);
                        int deficit = preferredLeaders.Count() - activeNodes.Count();
                        if (preferedMissing.Count() != 0)
                        {
                            var message = "Prefered leader ready to rejoin: " + preferedMissing.First();
                            Events.Add(Tuple.Create(DateTimeOffset.Now, message));
                            Trace.WriteLine(message);
                            var worstLearner = c.Proposers.OrderBy(l => _paxos.Scheduler.Now - LastContact(l)).Except(preferredLeaders).Take(1);
                            var nextProposers = c.Proposers.Except(worstLearner).Union(preferedMissing);
                            Reconfigure(nextProposers);
                        }
                        else if (deficit > 0)
                        {
                            var message = "Resiliance compromised. " + deficit.ToString() + " leaders not responding.";
                            Events.Add(Tuple.Create(DateTimeOffset.Now, message));
                            Trace.WriteLine(message);
                            // Find the closest unused learner and add him to the proposer set.
                            var bestLearner = c.Learners
                                                    .Except(c.Proposers)
                                                    .OrderBy(l => _paxos.Scheduler.Now - LastContact(l))
                                                    .Take(1);
                            var bestLeaders = c.Proposers.OrderBy(l => _paxos.Scheduler.Now - LastContact(l)).Take(preferredLeaders.Count() - 1);
                            var nextProposers = bestLeaders.Union(bestLearner);
                            Reconfigure(nextProposers);
                        }
                        else if (deficit < 0)
                        {
                            // There is a surplus, so shrink
                            var message = "Excess: " + (-deficit).ToString() + " extra leaders.";
                            Events.Add(Tuple.Create(DateTimeOffset.Now, message));
                            Trace.WriteLine(message);
                            var worstLearner = c.Proposers.OrderBy(l => _paxos.Scheduler.Now - LastContact(l)).Except(preferredLeaders).Take(1);
                            var nextProposers = c.Proposers.Except(worstLearner);
                            Reconfigure(nextProposers);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
            }
            finally
            {
                Interlocked.Decrement(ref CheckCount);
            }
        }

        public void Reconfigure(IEnumerable<string> nextProposers)
        {
            var message = "Next Proposers " + string.Join(", ", nextProposers);
            Events.Add(Tuple.Create(DateTimeOffset.Now, message));
            Trace.WriteLine(message);
            var proposal = new ProposalConfiguration<string>(new Configuration<string>(nextProposers, nextProposers, _paxos.ConfigurationHint.Learners));
            var task = this.WaitAsync(proposal, CancellationToken.None);
            var t = this.ReplicateAsync(proposal, CancellationToken.None);
            task.ContinueWith(a =>
            {
                if (a.Status == TaskStatus.RanToCompletion)
                {
                    Events.Add(Tuple.Create(DateTimeOffset.Now, "Reconfiguration successful"));
                    Trace.WriteLine("Reconfigure Succeeded");
                }
                else
                {
                    Events.Add(Tuple.Create(DateTimeOffset.Now, "Reconfiguration failed"));
                    Trace.WriteLine(Tuple.Create(a.Status, a.Exception));
                }
            });
        }

        public AdaptiveStateMachine(string self, IDictionary<string, Uri> endpoints, ISubject<Tuple<Uri, Message>> mesh, IStorage<string, T> storage, IEnumerable<string> preferedLeaders, FUSE.Paxos.Policy policy = null, IScheduler scheduler = null, Counters counters = null)
            : base(self, storage, preferedLeaders, null, policy, scheduler, counters: counters)
        {
            this.configurationTimer = new Timer(_ => this.CheckConfiguration(), null, configurationTimeout, configurationTimeout);
            this._disposables.Add(configurationTimer);
            this.preferredLeaders = preferedLeaders;
            this.EndpointNamesToUris = endpoints;
            this.EndpointUrisToNames = new Dictionary<Uri,string>();
            this.Mesh = mesh;
            foreach(var i in endpoints)
            {
                this.EndpointUrisToNames.Add(i.Value, i.Key);
            }

            _outgoing = Subject.Synchronize(new Subject<Tuple<string, Message>>(), _paxos.Scheduler);
            _disposables.Add(mesh.Subscribe(m =>
            {
                lock (LastContactByNode)
                {
                    if (this.EndpointUrisToNames.ContainsKey(m.Item1))
                    {
                        LastContactByNode[this.EndpointUrisToNames[m.Item1]] = _paxos.Scheduler.Now;
                    }
                }
            }, Validation.SwallowException, () => { }));           
        }

        public override IObserver<Tuple<string, Message>> Outgoing
        {
            get
            {
                return _outgoing;
            }
        }

        public IObservable<Tuple<string, Message>> LogRetryErrors(IObservable<Tuple<string, Message>> source)
        {
            return RX.LogRetryErrors(source, _paxos.Policy.BackoffMaximum, _paxos.Scheduler);
        }

        public override Task StartAsync(int count_executed)
        {
            using (new TimerScope("StateMachine.StartAsync", count_executed))
            {
                var incoming = Map(Mesh, this.EndpointUrisToNames);

                _disposables.Add(Map(_outgoing, this.EndpointNamesToUris).Subscribe(Mesh));

                _disposables.AddRange(_paxos.Agents(incoming, _random).Select(agent => LogRetryErrors(agent).Subscribe(_outgoing)));

                return base.StartAsync(count_executed);
            }
        }

        private readonly ReaderWriterLockSlim _mutex = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        public IObservable<Tuple<Target, Message>> Map<Source, Target>(IObservable<Tuple<Source, Message>> source, IDictionary<Source, Target> target_by_source)
        {
            return RX.Create<Tuple<Target, Message>>(observer =>
            {
                return source.Subscribe(
                    source_node_message =>
                    {
                        try
                        {
                            Target target_node;
                            bool found_target;
                            using (Concurrency.ReaderLock(_mutex))
                            {
                                found_target = target_by_source.TryGetValue(source_node_message.Item1, out target_node);
                            }
                            if (found_target)
                            {
                                var target_node_message = Tuple.Create(target_node, source_node_message.Item2);
                                observer.OnNext(target_node_message);
                            }
                        }
                        catch (Exception error)
                        {
                            observer.OnError(error);
                        }
                    },
                    observer.OnError,
                    observer.OnCompleted
                    );

            });
        }
    }
}