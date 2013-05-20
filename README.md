# Azure Active Directory (AAD) Availability Proxy for .Net

## What it does

The AAD Availability proxy increases the availability of a RESTful service by ensuring several identical instances of a service received the same operations in the same order.

## How it works

The proxy is a layer that runs on top of identical instances of a service.  The services could run on different machines, in different regions, on different hardware or even have different implementations.  All that matters is that the external behavior is exactly the same for a sequence of operations.  An operation is any HTTP verb, GET, PUT, POST, DELETE, PATCH.  All instances are available for all operations (unless the instance is down).

Access to each instance of the service is mediated by an instance of the proxy.  If the proxy receives a GET request, it forwards it through to the underlying service and streams the result back to the client.  If the proxy receives an update (PUT, POST, DELETE, PATCH), the proxy packages that operation up and sends it out to its peers.  

Updates can land at any node.  First, the peers determine a global ordering for each update and place them into a log.  There is an asynchronous process that applies the updates to the underlying service.  The updates are guarantee to be applied in the same order, but not at exactly the same time.  Every time an update completes, the process looks to see if anyone is waiting on the the result, and if so streams the result back to the client that initiated the operation.  All of that coordination comes at a price.  Latency increases for updates.  

As described so far, reads (GETs) are not guaranteed to be consistent.  The system addresses this in two ways.  First, if a node has fallen behind for any reason, it will refuse to process the request and throw a 503.  This mitigates the issue.  To completely solve the issue the CacheControl no-cache header can be set on a GET request.  This instructs an HTTP service to perform an authoritative read and ignore all cached results which might be stale.  With this option a GET is treated in the same way as an update, so the result is guarantee to be consistent.  The cost is also the same, latency increases.  

## How to use it

The proxy runs as an ASP.Net service that can be hosted in Azure.  First create instances of the services the proxy will keep synchronized.  Next create hosted service for each underlying instance.  These can be placed in the same affinity group, but this is not required.  The address and connection string for each instance of the proxy are specified in a service configuration.  

### Proxy configuration

**Name:** The name of the local instance (e.g. North)

**Services:** A list of name=value pairs for the name and type of each service (e.g. Movies=AzureStorage).  It is possible to have an instance of the proxy manage multiple services.  Also, different types of services can be managed.  Support for Azure storage (AzureStorage) and OData (DataService) are implemented.  Other services can be implemented easily.  The purpose of the layer is to fix up details that do not proxy well such as uri formats or authentication.  

**MeshMutualAuthThumbprint:** The instances of the proxy communicate over SSL and use mutual authentication.  This is the thumbprint of the certificate that is used.  It must be loaded onto the hosted service before the instances can run.

### Service configuration

There are several settings that are particular to each service represented by the proxy.  These settings begin with the service name.

**ServiceName.ResourcePresented:**  This is the domain name of the resource presented by all instances of the service (e.g. mymovies.trafficmanager.net) (See Routing below).

**ServiceName.PresentedAccount:** This is a made up name for the storage account presented.  It must conform to the Azure storage account naming convention  (e.g. mymovies).

**ServiceName.PresentedKey:** This is the key of the storage account presented by the proxy.  It must be a valid base64 encoded key.  You can reuse an azure storage account key or make up one of your own.

**ServiceName.Nodes:** A list of name=value pairs containing the name and address of each instance of the proxy (e.g. West=mymovies-west.cloudapp.net;South=mymovies-south.cloudapp.net;East=mymovies-east.cloudapp.net)

**ServiceName.PreferedActiveMembers** There can be a large number of instances of the proxy.  We typically run 6 or 8, but only 3 are required to achieve disaster recovery requirements.  The others act as hot backups that are available for reads and ready to step in if there is a failure, but are not required for each update.  This list contains the preferred active nodes.  The first node in the list will be the preferred leader.  (e.g. West;East;South)

### Instance Configuration

The following settings are particular to each instance of the service.

**Realm:** The address of the local instance (e.g. https://mymovies-north.cloudapp.net)  This is used for authentication to the management console.  The management console is a standard MVC app.  We use WS-Fed and ACS (Azure Access Control Service).  You can use what ever you'd like.

**StorageAccountName** and **StorageAccountKey:** The proxy keeps some state in an Azure storage account.  This state is particular to the proxy itself.  For example whether the instance is enabled or not and the current position in the log.  

**ServiceName.StorageAccount:** The name of the underlying account for the Azure storage service.

**ServiceName.StorageKey:** The key for the underlying storage account.

### Routing

The availability proxy will keep instances of the proxy up and running, but at any given time one or more may be unavailable-- that is the purpose of the proxy.  Some routing is required for a client to reach a running instance.  We use Azure Traffic Manager.  Any GTM which monitors availability will meet the requirement.

## Details of the Azure Storage Proxy

## About the code

Code hosted on GitHub under Apache 2.0 license

In order to build this demo application yourself, you will need to download:

- [Windows Azure SDK](http://www.windowsazure.com/en-us/develop/downloads/ "Windows Azure SDK")
- [Parallel Extension Extras](http://code.msdn.microsoft.com/ParExtSamples "Parallel Extensions Extras")
- [Fuse Labs Paxos and Weld](http://www.microsoft.com/en-us/download/details.aspx?id=38796 "Fuse Labs Paxos and Weld")

The build will look for these binaries in the ref directory.  You will need to adjust the references to the Azure SDK to match the version you have downloaded.

A number of other dependencies should be pulled in automatically via NuGet.