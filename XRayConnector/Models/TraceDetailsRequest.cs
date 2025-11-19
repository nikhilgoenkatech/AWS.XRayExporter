using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XRayConnector
{
    public class TraceDetailsRequest
    {
        public string[] TraceIds { get; set; }
        public string NextToken { get; set; }
    }
}
