// Copyright (c) Microsoft Corporation
using System.Web;
using System.Web.Mvc;

namespace ReverseProxy
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }
    }
}