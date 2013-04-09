// Copyright (c) Microsoft Corporation
using System;
using System.Diagnostics;
using Microsoft.Web.Administration;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;

namespace ReverseProxy
{
    public class WebRole : RoleEntryPoint
    {
        CloudDrive drive;
        public override bool OnStart()
        {
            Trace.WriteLine("WebRole.OnStart", "Error");
            CloudStorageAccount storageAccount;
            try
            {
                FUSE.Weld.Azure.Configuration.SetConfigurationSettingPublisher(); 
                storageAccount = Utility.GetStorageAccount();
                Trace.WriteLine("WebRole.OnStart: Initializing Cache","Verbose");

                var localCache = RoleEnvironment.GetLocalResource("DriveCache");
                CloudDrive.InitializeCache(localCache.RootPath, localCache.MaximumSizeInMegabytes);

                Trace.WriteLine("WebRole.OnStart: Creating Drive", "Verbose");
                drive = new CloudDrive(new Uri(storageAccount.BlobEndpoint + "root/proxy.vhd"), storageAccount.Credentials);
                drive.CreateIfNotExist(10 * 1000);

                Trace.WriteLine("WebRole.OnStart: Mounting Drive", "Verbose");
                var cloudDriveLetter = drive.Mount(localCache.MaximumSizeInMegabytes, DriveMountOptions.Force);
            }
            catch (Exception x)
            {
                Trace.TraceError("WebRole.OnStart:\n" + x.ToString());
            }

            return base.OnStart();
        }

        public override void OnStop()
        {
            Trace.Write("WebRole.OnStop", "Error");
            try
            {
                Trace.WriteLine("WebRole.OnStop: Unmounting drive", "Verbose");
                drive.Unmount();
                Trace.WriteLine("WebRole.OnStop: Removing node from traffic manager", "Verbose");
                // This should run in parallel
                foreach (var service in Global.stateMachines.Keys)
                {
                    ReverseProxy.OnOff.SetEndpointPolicy(service, false);
                }
                base.OnStop();
                Trace.WriteLine("WebRole.OnStop: Done", "Verbose");
            }
            catch (Exception x)
            {
                Trace.TraceError(x.ToString());
            }
        }

        public override void Run()
        {
            //using (ServerManager serverManager = new ServerManager())
            //{
            //    var mainSite = serverManager.Sites[RoleEnvironment.CurrentRoleInstance.Id + "_Web"];
            //    var mainApplication = mainSite.Applications["/"];
            //    var mainApplicationPool = serverManager.ApplicationPools[mainApplication.ApplicationPoolName];

            //    mainApplicationPool.Failure.RapidFailProtection = false;

            //    serverManager.CommitChanges();
            //}

            base.Run();
        }

    }

    public static class Extensions2
    {
        public static void Write(this CloudQueue queue, string message)
        {
            queue.AddMessage(new CloudQueueMessage(RoleEnvironment.CurrentRoleInstance.Id + " " + message), TimeSpan.FromDays(1));
        }
    }
    
}