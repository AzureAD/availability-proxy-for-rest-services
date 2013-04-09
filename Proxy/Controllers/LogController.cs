// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ReverseProxy.Models;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Net;
using FUSE.Paxos;

namespace ReverseProxy.Controllers
{
    [NoCache]
    [HandleError]
    public class LogController : Controller
    {
        //
        // GET: /Log/

        public ActionResult Index(string service, int? page)
        {
            ViewBag.Location = RoleEnvironment.GetConfigurationSettingValue("Name");
            ViewBag.Service = service;

            AdaptiveStateMachine<SerialilzableWebRequest, HttpStatusCode> stateMachine;
            if (!Global.stateMachines.TryGetValue(service, out stateMachine) || stateMachine == null)
            {
                return View("ServiceNotPresent");
            }

            var storage = stateMachine.Paxos.Storage;
            // Need to make sure that this does not materialize the entire log
            var messages = storage.LearnedInstances.Reverse().Select(i => new LogModel { Instance = i, Message = FormatOp(storage.Learned(i)) });
            var pages = new PaginatedList<LogModel>(messages, page ?? 0, 25);
            return View(pages);
        }

        private string FormatOp(Message.Learn<string, SerialilzableWebRequest> op)
        {
            var configuration = op.proposal as ProposalConfiguration<string>;
            var update = op.proposal as Proposal<string, SerialilzableWebRequest>;
            var stop = op.proposal as ProposalStop;
            var noop = op.proposal as ProposalNoOperation<string>;

            if (configuration != null)
            {
                return "Configuration " + configuration.configuration.ToString();
            }
            else if (update != null)
            {
                return "Update " + update.value.ToString();
            }
            else if (stop != null)
            {
                return "Stop";
            }
            else if (noop != null)
            {
                return "No Op";
            }
            else
            {
                return "Unknown";
            }
        }

        //
        // GET: /Log/Details/5

        public ActionResult Details(string service, int id)
        {
            ViewBag.Location = RoleEnvironment.GetConfigurationSettingValue("Name");
            ViewBag.Service = service;

            AdaptiveStateMachine<SerialilzableWebRequest, HttpStatusCode> stateMachine;
            if (!Global.stateMachines.TryGetValue(service, out stateMachine))
            {
                return View("Error");
            }

            var item = stateMachine.Paxos.Storage.Learned(id).proposal as Proposal<string, SerialilzableWebRequest>;
            if (item != null)
            {
                return View(item.value);
            }
            else
            {
                return View("NoDetails");
            }
        }

        public ActionResult Live(string service)
        {
            ViewBag.Location = RoleEnvironment.GetConfigurationSettingValue("Name");
            ViewBag.Service = service;

            return View(new LiveLogModel { Service = service });
        }
    }
}
