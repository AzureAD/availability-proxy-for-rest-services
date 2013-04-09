// Copyright (c) Microsoft Corporation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace ReverseProxy
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            //routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            //routes.MapHttpRoute(
            //    name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{id}",
            //    defaults: new { id = RouteParameter.Optional }
            //);
            routes.MapRoute(
                "Paged",
                "noc2/{service}/{controller}/Page/{page}",
                new { action = "Index" }
            );


            routes.MapRoute(
                name: "Default",
                url: "noc2/{service}/{controller}/{action}/{id}",
                defaults: new { service = Helpers.Services().Keys.First(), controller = "Status", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}