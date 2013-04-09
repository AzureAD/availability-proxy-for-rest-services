using System.Collections.Specialized;
using System.Net;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ReverseProxy
{
    public static class Helpers
    {
        public static string CurrentLocation()
        {
            return RoleEnvironment.GetConfigurationSettingValue("Name");
        }

        public static IDictionary<string, string> Nodes(string service)
        {
            return ParseAList(RoleEnvironment.GetConfigurationSettingValue(service + ".Nodes"));
        }

        public static IDictionary<string, string> Services()
        {
            return ParseAList(RoleEnvironment.GetConfigurationSettingValue("Services"));
        }

        public static IDictionary<string, string> ParseAList(string alist)
        {
            try
            {
            var ret = new Dictionary<string, string>();
            foreach (var node in alist.Split(';'))
            {
                var nk = node.Split('=');
                ret[nk[0]] = nk[1];
            }
            return ret;
            }
            catch (Exception)
            {
                throw new ArgumentException("The input string is not in the form of key1=val1;key2=val2: " + alist);
            }
        }

        public static void AddUnrestricted(this NameValueCollection to, NameValueCollection from)
        {
            foreach (var h in from.AllKeys)
            {
                if (!WebHeaderCollection.IsRestricted(h))
                {
                    foreach (var v in from.GetValues(h))
                    {
                        to.Add(h, v);
                    }
                }
            }
        }
    }
}