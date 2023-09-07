using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.XRay;
using Amazon.XRay.Model;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace XRayConnector
{
    public class XRayConnector
    {

        private readonly IHttpClientFactory _httpClientFactory;
        public XRayConnector(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<TracesResult> GetTraces(GetTraceSummariesRequest req, ILogger log)
        {

            var xray = new AmazonXRayClient(Environment.GetEnvironmentVariable("AWS_IdentityKey"), Environment.GetEnvironmentVariable("AWS_SecretKey"));
            
            var resp = await xray.GetTraceSummariesAsync(req);

            if (resp.TraceSummaries.Count > 0)
            {
                var traceIds = new List<string>(resp.TraceSummaries.Count);
                foreach (var s in resp.TraceSummaries)
                    traceIds.Add(s.Id);


                var res = new TracesResult();
                res.TraceIds = traceIds.Chunk(5); //provide result in a batch of 5 id's due to api limits: https://docs.aws.amazon.com/xray/latest/api/API_BatchGetTraces.html
                res.NextToken = resp.NextToken;

                return res;
            }
            else
                return null;
            
        }

        [FunctionName(nameof(GetRecentTraceIds))]
        public async Task<TracesResult> GetRecentTraceIds([ActivityTrigger] TracesRequest req, ILogger log)
        {
            var reqObj = new GetTraceSummariesRequest
            {
                StartTime = req.StartTime,
                EndTime = req.EndTime,
                NextToken = req.NextToken
            };

            return await GetTraces(reqObj, log);
        }
      

        async Task<TraceDetailsResult> GetTraceDetails(BatchGetTracesRequest req, ILogger log)
        {
            var xray = new AmazonXRayClient(Environment.GetEnvironmentVariable("AWS_IdentityKey"), Environment.GetEnvironmentVariable("AWS_SecretKey"));
            BatchGetTracesResponse resp = await xray.BatchGetTracesAsync(req);
            
            //serialize segements into a json array, to avoid additional (de)serialization overhead
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            bool isFirst = true;
            foreach (var t in resp.Traces)
            {
                foreach (var s in t.Segments)
                {
                    if (!isFirst)
                        sb.Append(',');
                    sb.Append(s.Document);
                    isFirst = false; ;
                }
            }
            sb.Append(']');

            var res = new TraceDetailsResult
            {
                Traces = sb.ToString(),
                NextToken = resp.NextToken
            };

            return res;
        }


        [FunctionName(nameof(GetTraceDetails))]
        public Task<TraceDetailsResult> GetTraceDetails([ActivityTrigger] TraceDetailsRequest req, ILogger log)
        {
            var reqObj = new BatchGetTracesRequest()
            {
                TraceIds = new List<string>(req.TraceIds), 
                NextToken = req.NextToken
            };

            return GetTraceDetails(reqObj, log);
        }


        [FunctionName(nameof(ProcessTraces))]
        public async Task<bool> ProcessTraces([ActivityTrigger] string tracesJson, ILogger log)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(tracesJson);

                var conv = new XRay2OTLP.Convert(null, false);
                var exportTraceServiceRequest = conv.FromXRaySegmentDocArray(jsonDoc);

                var httpClient = _httpClientFactory.CreateClient("XRayConnector");

                var authHeader = Environment.GetEnvironmentVariable("OTLP_HEADER_AUTHORIZATION");
                if (!String.IsNullOrEmpty(authHeader))
                    httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

                var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT");
                if (!otlpEndpoint.Contains("v1/traces"))
                    if (otlpEndpoint.EndsWith("/"))
                        otlpEndpoint = otlpEndpoint += "v1/traces";
                    else
                        otlpEndpoint = otlpEndpoint += "/v1/traces";

                var content = new XRay2OTLP.ExportRequestContent(exportTraceServiceRequest);

                var res = await httpClient.PostAsync(otlpEndpoint, content);
                if (!res.IsSuccessStatusCode)
                {
                    throw new Exception("Couldn't send span " + (res.StatusCode));
                }

            }catch (Exception e)
            {
                log.LogError(e, "Couldn't process tracedetails");

                return false;
            }
            return true;
        }

        [FunctionName(nameof(RetrieveTraceDetails))]
        public async Task RetrieveTraceDetails(
        [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            var traces = context.GetInput<TracesResult>();
            if (traces != null)
            {
                foreach (var traceBatch in traces.TraceIds)
                {
                    var getTraceDetails = new TraceDetailsRequest()
                    {
                        TraceIds = traceBatch
                    };
                    var traceDetails = await context.CallActivityAsync<TraceDetailsResult>(nameof(GetTraceDetails), getTraceDetails);
                    await context.CallActivityAsync<bool>(nameof(ProcessTraces), traceDetails.Traces);

                    string nextToken = traceDetails.NextToken;
                    while (!String.IsNullOrEmpty(nextToken))
                    {
                        getTraceDetails.NextToken = nextToken;
                        traceDetails = await context.CallActivityAsync<TraceDetailsResult>(nameof(GetTraceDetails), getTraceDetails);
                        await context.CallActivityAsync<bool>(nameof(ProcessTraces), traceDetails.Traces);
                        nextToken = traceDetails.NextToken;
                    }
                }
            }
            
        }

        [FunctionName(nameof(RetrieveRecentTraces))]
        public async Task RetrieveRecentTraces(
          [OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {

            var currentTime = context.CurrentUtcDateTime; //https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp#dates-and-times
            var getTraces = new TracesRequest()
            {
                StartTime = currentTime.AddMinutes(-5),
                EndTime = currentTime
            };
            
            var traces = await context.CallActivityAsync<TracesResult>(nameof(GetRecentTraceIds), getTraces);
            if (traces != null)
            {
                await context.CallSubOrchestratorAsync(nameof(RetrieveTraceDetails), traces);

                string nextTraceBatch = traces.NextToken;
                while (!String.IsNullOrEmpty(nextTraceBatch))
                {
                    getTraces.NextToken = nextTraceBatch;
                    var nextTraces = await context.CallActivityAsync<TracesResult>(nameof(GetRecentTraceIds), getTraces);

                    if (nextTraces != null)
                        await context.CallSubOrchestratorAsync(nameof(RetrieveTraceDetails), nextTraces);

                }
            }

        }

        async Task<string> Execute(IDurableOrchestrationClient starter, ILogger log)
        {
            string instanceId = await starter.StartNewAsync(nameof(RetrieveRecentTraces), null);

            log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);

            return instanceId;
        }


        //Do not use timer triggered functions to avoid overlap issues: https://stackoverflow.com/a/62640692
        [FunctionName(nameof(ScheduledStart))]
        public async Task ScheduledStart([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        {
            await Execute(starter, log);
        }

#region Testing
#if DEBUG


        private decimal ToEpochSeconds(DateTime ts)
        {
            // Get epoch second as 32bit integer
            const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;
            const long MicrosecondPerSecond = TimeSpan.TicksPerSecond / TicksPerMicrosecond;

            DateTime _epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long _unixEpochMicroseconds = _epochStart.Ticks / TicksPerMicrosecond;

            long microseconds = ts.Ticks / TicksPerMicrosecond;
            long microsecondsSinceEpoch = microseconds - _unixEpochMicroseconds;
            return (decimal)microsecondsSinceEpoch / MicrosecondPerSecond;
        }
        private string NewXrayTraceId()
        {

            const int Version = 1;
            const int RandomNumberHexDigits = 24; // 96 bits
            const char Delimiter = '-';

            // Get epoch second as 32bit integer
            int epoch = (int)ToEpochSeconds(DateTime.UtcNow);


            // Get a 96 bit random number
            var rnd = new Random();

            byte[] bytes = new byte[RandomNumberHexDigits / 2];
            rnd.NextBytes(bytes);

            string randomNumber = string.Concat(bytes.Select(x => x.ToString("x2", CultureInfo.InvariantCulture)).ToArray());
            
            string[] arr = { Version.ToString(CultureInfo.InvariantCulture), epoch.ToString("x", CultureInfo.InvariantCulture), randomNumber };

            // Concatenate elements with dash
            return string.Join(Delimiter.ToString(), arr);
        }


        [FunctionName(nameof(TestGenerateSampleTrace))]
        public async Task<HttpResponseMessage> TestGenerateSampleTrace(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestMessage req,
            ILogger log)
        {
            log.LogWarning(nameof(TestGenerateSampleTrace));

            var xray = new AmazonXRayClient(Environment.GetEnvironmentVariable("AWS_IdentityKey"), Environment.GetEnvironmentVariable("AWS_SecretKey"));
            PutTraceSegmentsRequest seg = new PutTraceSegmentsRequest();
            string rootSegment = "{\"id\":\"194fcc8747581230\",\"name\":\"Scorekeep\",\"start_time\":@S1,\"end_time\":@E1,\"http\":{\"request\":{\"url\":\"http://scorekeep.elasticbeanstalk.com/api/user\",\"method\":\"POST\",\"user_agent\":\"Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/59.0.3071.115 Safari/537.36\",\"client_ip\":\"205.251.233.183\"},\"response\":{\"status\":200}},\"aws\":{\"elastic_beanstalk\":{\"version_label\":\"app-abb9-170708_002045\",\"deployment_id\":406,\"environment_name\":\"scorekeep-dev\"},\"ec2\":{\"availability_zone\":\"us-west-2c\",\"instance_id\":\"i-0cd9e448944061b4a\"},\"xray\":{\"sdk_version\":\"1.1.2\",\"sdk\":\"X-Ray for Java\"}},\"service\":{},\"trace_id\":\"@TRACEID\",\"user\":\"5M388M1E\",\"origin\":\"AWS::ElasticBeanstalk::Environment\",\"subsegments\":[{\"id\":\"0c544c1b1bbff948\",\"name\":\"Lambda\",\"start_time\":@S1_1,\"end_time\":@E1_1,\"http\":{\"response\":{\"status\":200,\"content_length\":14}},\"aws\":{\"log_type\":\"None\",\"status_code\":200,\"function_name\":\"random-name\",\"invocation_type\":\"RequestResponse\",\"operation\":\"Invoke\",\"request_id\":\"ac086670-6373-11e7-a174-f31b3397f190\",\"resource_names\":[\"random-name\"]},\"namespace\":\"aws\"},{\"id\":\"071684f2e555e571\",\"name\":\"## UserModel.saveUser\",\"start_time\":@S1_1,\"end_time\":@E1_1,\"metadata\":{\"debug\":{\"test\":\"Metadata string from UserModel.saveUser\"}},\"subsegments\":[{\"id\":\"4cd3f10b76c624b4\",\"name\":\"DynamoDB\",\"start_time\":@S1_1_1,\"end_time\":@E1_1_1,\"http\":{\"response\":{\"status\":200,\"content_length\":57}},\"aws\":{\"table_name\":\"scorekeep-user\",\"operation\":\"UpdateItem\",\"request_id\":\"MFQ8CGJ3JTDDVVVASUAAJGQ6NJ82F738BOB4KQNSO5AEMVJF66Q9\",\"resource_names\":[\"scorekeep-user\"]},\"namespace\":\"aws\"}]}]}";

            var start = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(60));

            var updated = rootSegment.Replace("@TRACEID", NewXrayTraceId())
                                    .Replace("@S1_1_1", ToEpochSeconds(start.AddSeconds(1)).ToString().Replace(',', '.'))
                                    .Replace("@E1_1_1", ToEpochSeconds(start.AddSeconds(3)).ToString().Replace(',', '.'))
                                    .Replace("@S1_1", ToEpochSeconds(start.AddSeconds(1)).ToString().Replace(',', '.'))
                                    .Replace("@E1_1", ToEpochSeconds(start.AddSeconds(2)).ToString().Replace(',','.'))
                                    .Replace("@S1", ToEpochSeconds(start).ToString().Replace(',', '.'))
                                    .Replace("@E1", ToEpochSeconds(start.AddSeconds(3)).ToString().Replace(',', '.'));
                                                     

            seg.TraceSegmentDocuments.Add(updated);
            var resp = await xray.PutTraceSegmentsAsync(seg);
            
            var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            res.Content = new StringContent("{status:\"ok\"}", null, "application/json");

            return res;
        }

        [FunctionName(nameof(TestStart))]
        public async Task<HttpResponseMessage> TestStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            log.LogWarning("TestStart");

            string instanceId = await Execute(starter, log);
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

      
#endif
#endregion

    }
}