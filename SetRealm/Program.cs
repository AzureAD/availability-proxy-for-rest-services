//-//-------------------------------------------------------------------------------------------------
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
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.WindowsAzure.ServiceRuntime;
// Copyright (c) Microsoft Corporation
using System.Threading;
using System.Xml.Linq;
using System.Collections;

namespace SetRealm
{
    class Program
    {
        const string roleNs = "{http://schemas.microsoft.com/ServiceHosting/2008/10/ServiceDefinition}";
        public static IEnumerable<string> AzureWebSiteDirectories
        {
            get
            {
                string roleRootDir = Environment.GetEnvironmentVariable("RdRoleRoot");

                XDocument roleModelDoc = XDocument.Load(Path.Combine(roleRootDir, "RoleModel.xml"));
                var siteElements = roleModelDoc.Root.Element(roleNs + "Sites").Elements(roleNs + "Site");

                return
                    from siteElement in siteElements
                    where siteElement.Attribute("name") != null
                            && siteElement.Attribute("name").Value == "Web"
                            && siteElement.Attribute("physicalDirectory") != null
                    select Path.Combine(roleRootDir, siteElement.Attribute("physicalDirectory").Value);
            }
        }

        static void PrintEnv(StreamWriter log, IDictionary env)
        {
            foreach (var k in env.Keys)
            {
                log.WriteLine(k + " " + env[k]);
            }
        }

        static void Main(string[] args)
        {
            using (var log = File.CreateText(Environment.GetEnvironmentVariable("Temp") + @"\SetRealmLog.txt"))
            {
                log.WriteLine("In SetRealm");
                Console.WriteLine(String.Join(", ", args));
                PrintEnv(log, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine));
                PrintEnv(log, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process));
                PrintEnv(log, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User));
                foreach (var dir in AzureWebSiteDirectories)
                {
                    try
                    {
                        var fileName = Path.Combine(dir, "Web.config").ToString();
                        log.WriteLine(fileName);
                        string config;
                        using (var file = File.OpenRead(fileName))
                        using (var reader = new StreamReader(file))
                        {
                            config = reader.ReadToEnd();
                        }
                        log.WriteLine("Read");
                        if (config.Contains("urn:Dummy"))
                        {
                            log.WriteLine("Contains Dummy");
                            var realm = Environment.GetEnvironmentVariable("RealmEnv");
                            log.WriteLine(realm);

                            config = config.Replace("urn:Dummy", realm);

                            using (var file = File.OpenWrite(fileName))
                            using (var writer = new StreamWriter(file))
                            {
                                log.WriteLine("Open for write");
                                writer.Write(config);
                            }
                            log.WriteLine("Written");
                        }
                        else
                        {
                            log.WriteLine("Does not contain Dummy");
                        }
                    }
                    catch(Exception e)
                    {
                        log.WriteLine(e.Message);
                        log.WriteLine(e.StackTrace);
                    }
                }
                log.Flush();
                log.Close();
            }

        }
    }
}
