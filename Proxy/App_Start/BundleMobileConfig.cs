// Copyright (c) Microsoft Corporation
using System.Web;
using System.Web.Optimization;

namespace ReverseProxy {
    public class BundleMobileConfig {
        public static void RegisterBundles(BundleCollection bundles) {
            bundles.Add(new ScriptBundle("~/bundles/jquerymobile")
                .Include("~/Scripts/jquery.mobile-{version}.js"));

            bundles.Add(new StyleBundle("~/Content/Mobile/css")
                .Include("~/Content/Site.Mobile.css"));
            
            bundles.Add(new StyleBundle("~/Content/jquerymobile/css")
                .Include("~/Content/jquery.mobile-{version}.css"));
        }
    }
}