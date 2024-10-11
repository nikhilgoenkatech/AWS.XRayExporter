using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XRayConnector
{
    public class TracesResult
    {
        public string NextToken { get; set; }
        public IEnumerable<string[]> TraceIds { get; set; }
    }
}
