// Copyright (c) Microsoft Corporation
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace ReverseProxy
{
    public class AzureStorageProxyBase : ProxyBase 
    {
        protected string resourcePresented;
        protected string prefix;
        protected string PresentedAccount;
        protected string PresentedKey;
        protected string ManagedAccount;
        protected string ManagedKey;

        protected AzureStorageProxyBase(string resourcePresented, string prefix)
        {
            this.resourcePresented = resourcePresented;
            this.prefix = prefix;
        }

        public AzureStorageProxyBase(string resourcePresented, string prefix, string presentedAccount, string presentedKey, string managedAccount, string managedKey)
        {
            this.resourcePresented = resourcePresented;
            this.prefix = prefix;
            this.PresentedAccount = presentedAccount;
            this.PresentedKey = presentedKey;
            this.ManagedAccount = managedAccount;
            this.ManagedKey = managedKey;
        }

        protected override bool ShouldRewrite(string contentType)
        {
            return
                contentType.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.StartsWith("application/atom+xml", StringComparison.OrdinalIgnoreCase) ||
                contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        }

        protected override string RewriteResponse(string responseContent, string path)
        {
            var p = path.Split('/');
            var service = p[0].ToLower();
            var host = String.Format("{0}.{1}.core.windows.net", ManagedAccount, service);
            return responseContent.Replace(host, resourcePresented + "/" + prefix + "/" + service);
        }

        protected override Uri BuildUri(SerialilzableWebRequest request)
        {
            var p = request.Path.Split('/');
            var service = p.Length > 0 ? p[0].ToLower() : "";
            var host = String.Format("{0}.{1}.core.windows.net", ManagedAccount, service);
            var path = String.Join("/", p.Skip(1));

            var ub = new UriBuilder() { Host = host, Path = path, Query = request.Query };
            return ub.Uri;
        }

        protected override void AugmentHeaders(SerialilzableWebRequest request, HttpWebRequest remoteRequest)
        {
            var p = request.Path.Split('/');
            var service = p.Length > 0 ? p[0].ToLower() : "";
            var copyHeader = request.Headers.Get("x-ms-copy-source");
            if (copyHeader != null)
            {
                var copyPath = copyHeader.Split('/');
                if (copyPath.Length > 4 && copyPath[3] == copyPath[5])
                {
                    copyPath = copyPath.Skip(5).ToArray();
                    copyPath[0] = "";
                }
                copyPath[1] = ManagedAccount;
                remoteRequest.Headers.Set("x-ms-copy-source", String.Join("/", copyPath));
            }
            var authHeader = request.Headers.Get("Authorization");
            // Only sign a request that contains a verified signature.
            if (authHeader != null)
            {
                remoteRequest.Headers.Set("Authorization", SharedKeyAuthorizationHeader(authHeader.StartsWith("SharedKeyLite"), ManagedAccount, ManagedKey, remoteRequest.Method, remoteRequest.Headers, remoteRequest.RequestUri, remoteRequest.ContentLength, service == "table"));
            }
        }

        protected override string ExtractPath(string path)
        {
            var p = path.Split('/');
            // this works around features of AzureStorageClient with custom endpoints
            if (p.Length >= 6 && p[2].ToLower() == p[4].ToLower() && p[1].ToLower() == p[3].ToLower())
            {                
                return String.Join("/", p.Skip(p.Length == 9 ? 6 : 4));
            }
            else
            {
                return String.Join("/", p.Skip(2));
            }
        }

        protected override bool _authenticate(HttpContext context)
        {
            var request = context.Request;
            var p = request.Url.AbsolutePath.Split('/');
            var service = p[2].ToLower();

            var authHeader = request.Headers.Get("Authorization");
            // If there is no authentication header, it may be a public blob so let a GET go through,
            // but be sure not to sign somethign that didn't already have a verified signature.
            if (authHeader == null)
            {
                if (request.HttpMethod.Equals("get", StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            var computedSignature = SharedKeyAuthorizationHeader(authHeader.StartsWith("SharedKeyLite"), PresentedAccount, PresentedKey, request.HttpMethod, request.Headers, request.Url, request.ContentLength, service == "table");

            if (authHeader != computedSignature)
            {
                var encodedUri = (new UriBuilder(request.Url) { Path = request.Url.AbsolutePath.Replace(":", "%3A").Replace("@", "%40") }).Uri;
                var computedSignature2 = SharedKeyAuthorizationHeader(authHeader.StartsWith("SharedKeyLite"), PresentedAccount, PresentedKey, request.HttpMethod, request.Headers, encodedUri, request.ContentLength, service == "table");
                if (authHeader == computedSignature2)
                {
                    return true;
                }
                {
                    return false;
                }
            }
            return true;
        }

        public static string SharedKeyAuthorizationHeader(bool lite, string StorageAccount, string StorageKey, string method, NameValueCollection Headers, Uri uri, long contentLength, bool IsTableStorage = false, string contentType = "")
        {

            string MessageSignature;

            string algorithm = lite ? "SharedKeyLite " : "SharedKey ";

            var ifMatch = Headers.Get("If-Match") ?? "";
            var md5 = Headers.Get("Content-MD5") ?? "";

            if (IsTableStorage)
            {

                // One of the following is required
                string now;
                if (Headers.AllKeys.Contains("Date"))
                {
                    now = Headers["Date"];
                }
                else
                {
                    now = Headers["x-ms-date"];
                }

                if (lite)
                {
                    MessageSignature = String.Format("{0}\n{1}",
                                                     now,
                                                     GetCanonicalizedResource(uri, StorageAccount, IsTableStorage)
                        );
                }
                else
                {
                    MessageSignature = String.Format("{0}\n\n{1}\n{2}\n{3}",
                                                     method,
                                                     "application/atom+xml",
                                                     now,
                                                     GetCanonicalizedResource(uri, StorageAccount, IsTableStorage)
                        );
                }
            }
            else
            {
                if (lite)
                {
                    MessageSignature = String.Format("{0}\n{1}\n{2}\n{3}\n{4}{5}",
                                                     method,
                                                     md5,
                                                     contentType,
                                                     "",  // Is now used
                                                     GetCanonicalizedHeaders(Headers),
                                                     GetCanonicalizedResource(uri, StorageAccount, IsTableStorage)
                        );
                }
                else
                {
                    MessageSignature = String.Format("{0}\n\n\n{1}\n{5}\n\n\n\n{2}\n\n\n\n{3}{4}",
                                                     method,
                                                     (method == "GET" ||
                                                      method == "HEAD" ||
                                                      method == "DELETE" && (Headers["User-Agent"] ?? String.Empty).StartsWith("WA-Storage", StringComparison.OrdinalIgnoreCase))
                                                         ? String.Empty : contentLength.ToString(),
                                                     ifMatch,
                                                     GetCanonicalizedHeaders(Headers),
                                                     GetCanonicalizedResource(uri, StorageAccount, IsTableStorage),
                                                     md5
                        );
                }

            }

            byte[] SignatureBytes = System.Text.Encoding.UTF8.GetBytes(MessageSignature);
            System.Security.Cryptography.HMACSHA256 SHA256 = new System.Security.Cryptography.HMACSHA256(Convert.FromBase64String(StorageKey));
            String AuthorizationHeader = algorithm + StorageAccount + ":" + Convert.ToBase64String(SHA256.ComputeHash(SignatureBytes));
            return AuthorizationHeader;
        }

        public static string GetCanonicalizedHeaders(NameValueCollection headers)
        {
            ArrayList headerNameList = new ArrayList();
            StringBuilder sb = new StringBuilder();
            foreach (string headerName in headers.Keys)
            {
                if (headerName.ToLowerInvariant().StartsWith("x-ms-", StringComparison.Ordinal))
                {
                    headerNameList.Add(headerName.ToLowerInvariant());
                }
            }
            headerNameList.Sort();
            foreach (string headerName in headerNameList)
            {
                StringBuilder builder = new StringBuilder(headerName);
                string separator = ":";
                foreach (string headerValue in GetHeaderValues(headers, headerName))
                {
                    string trimmedValue = headerValue.Replace("\r\n", String.Empty);
                    builder.Append(separator);
                    builder.Append(trimmedValue);
                    separator = ",";
                }
                sb.Append(builder.ToString());
                sb.Append("\n");
            }
            return sb.ToString();
        }

        // Get header values.

        public static ArrayList GetHeaderValues(NameValueCollection headers, string headerName)
        {
            ArrayList list = new ArrayList();
            string[] values = headers.GetValues(headerName);
            if (values != null)
            {
                foreach (string str in values)
                {
                    list.Add(str.TrimStart(null));
                }
            }
            return list;
        }


        // Get canonicalized resourcePresented.

        public static string GetCanonicalizedResource(Uri address, string accountName, bool IsTableStorage)
        {
            StringBuilder str = new StringBuilder();
            StringBuilder builder = new StringBuilder("/");
            builder.Append(accountName);
            builder.Append(address.AbsolutePath);
            str.Append(builder.ToString());
            NameValueCollection values2 = new NameValueCollection();
            if (!IsTableStorage)
            {
                NameValueCollection values = HttpUtility.ParseQueryString(address.Query);
                foreach (string str2 in values.Keys)
                {
                    ArrayList list = new ArrayList(values.GetValues(str2));
                    list.Sort();
                    StringBuilder builder2 = new StringBuilder();
                    foreach (object obj2 in list)
                    {
                        if (builder2.Length > 0)
                        {
                            builder2.Append(",");
                        }
                        builder2.Append(obj2.ToString());
                    }
                    values2.Add((str2 == null) ? str2 : str2.ToLowerInvariant(), builder2.ToString());
                }
            }
            ArrayList list2 = new ArrayList(values2.AllKeys);
            list2.Sort();
            foreach (string str3 in list2)
            {
                StringBuilder builder3 = new StringBuilder(string.Empty);
                builder3.Append(str3);
                builder3.Append(":");
                builder3.Append(values2[str3]);
                str.Append("\n");
                str.Append(builder3.ToString());
            }
            return str.ToString();
        }
    }
}