using System;
using System.Collections.Generic;
using System.Text;

namespace XRay
{

    internal static class HttpAttributes
    {
        public const string Request = "request";
        public const string Method = "method";
        public const string Url = "url";
        public const string UserAgent = "user_agent";
        public const string ClientIp = "client_ip";
        public const string XForwardFor = "x_forwarded_for";
        public const string Traced = "traced";
        
        public const string Response = "response";
        public const string Status = "status";
        public const string ContentLength = "content_length";
    }
}
