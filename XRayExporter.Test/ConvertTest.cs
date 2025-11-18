using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json.Linq;
using Opentelemetry.Proto.Collector.Trace.V1;
using System.Diagnostics;

namespace XRayExporter.Test
{
    public class ConvertTest
    {
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void ConvertFromSample()
        {
            var tracesJson = File.ReadAllText("data/batchgettraces.json");
            var conv = new XRay2OTLP.Convert(null);
            var otlp = conv.FromXRay(tracesJson);

            //Assert.Pass();
        }

    }
}