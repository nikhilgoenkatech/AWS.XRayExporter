using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.SecurityToken.Model;
using Amazon.SecurityToken;
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
        private const string PeriodicAPIPollerSingletoninstanceId = "SinglePeriodicAPIPoller";
        private const string AWSIdentityKey = "AWS_IdentityKey";
        private const string AWSSecretKey = "AWS_SecretKey";
        private const string AWSRegionEndpoint = "AWS_RegionEndpoint";
        private const string AWSRoleArn = "AWS_RoleArn";
        private const string AWSRoleSessionDurationSeconds = "AWS_RoleSessionDurationSeconds";
        private const string PollingIntervalSeconds = "PollingIntervalSeconds";
        private const string PollingIntervalMinutes = "PollingIntervalMinutes"; 

        private const string AWSRoleSession = PeriodicAPIPollerSingletoninstanceId;

        private readonly AmazonXRayClient _xrayClient;
        private readonly IHttpClientFactory _httpClientFactory;

        private static SessionAWSCredentials _sessionCredentials;
        private static DateTime _credentialsExpiration;

        public XRayConnector(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _xrayClient = InitializeXRayClient().GetAwaiter().GetResult();
        }

        private async Task<AmazonXRayClient> InitializeXRayClient()
        {
            // Check if AWSRoleArn is set; if not, skip AssumeRole
            var roleArn = Environment.GetEnvironmentVariable(AWSRoleArn);
            var regionEndpoint = Environment.GetEnvironmentVariable(AWSRegionEndpoint);


            if (!string.IsNullOrEmpty(roleArn) && !string.IsNullOrEmpty(regionEndpoint))
            {
                try
                {
                    var sessionCredentials = await GetAWSCredentials();

                    return new AmazonXRayClient(
                        sessionCredentials, 
                        Amazon.RegionEndpoint.GetBySystemName(regionEndpoint));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("An unexpected error occurred while initializing the X-Ray client with assume role.", ex);
                }
            }
            else
            {
                var identityKey = Environment.GetEnvironmentVariable(AWSIdentityKey);
                var secretKey = Environment.GetEnvironmentVariable(AWSSecretKey);
                if (!String.IsNullOrEmpty(identityKey) && !String.IsNullOrEmpty(secretKey))
                {
                    if (String.IsNullOrEmpty(regionEndpoint))
                    {
                        return new AmazonXRayClient(
                            identityKey,
                            secretKey,
                            Amazon.RegionEndpoint.GetBySystemName(regionEndpoint));
                    }
                    else
                    {
                        return new AmazonXRayClient(
                            identityKey,
                            secretKey);
                    }
                }

            }

            return null;
        }

        private static async Task<SessionAWSCredentials> GetAWSCredentials()
        {
            if (_sessionCredentials == null || DateTime.UtcNow >= _credentialsExpiration)
            {
                var stsClient = new AmazonSecurityTokenServiceClient();

                int sessionDuration;
                if (!Int32.TryParse(Environment.GetEnvironmentVariable(AWSRoleSessionDurationSeconds), out sessionDuration))
                    sessionDuration = 3600;
                else if (sessionDuration < 600)
                    sessionDuration = 600;

                var assumeRole = new AssumeRoleRequest
                {
                    RoleArn = Environment.GetEnvironmentVariable(AWSRoleArn),
                    RoleSessionName = AWSRoleSession,
                    DurationSeconds = sessionDuration
                };

                if (!String.IsNullOrEmpty(assumeRole.RoleArn))
                {
                    try
                    {
                        var assumeRoleResponse = await stsClient.AssumeRoleAsync(assumeRole);

                        _sessionCredentials = new SessionAWSCredentials(
                            assumeRoleResponse.Credentials.AccessKeyId,
                            assumeRoleResponse.Credentials.SecretAccessKey,
                            assumeRoleResponse.Credentials.SessionToken
                        );

                        // Set expiration time (5 minutes before actual expiration)
                        _credentialsExpiration = DateTime.UtcNow.AddSeconds(assumeRoleResponse.Credentials.Expiration.Subtract(DateTime.UtcNow).TotalSeconds - 300);

                    }
                    catch (AmazonSecurityTokenServiceException ex)
                    {
                        throw new InvalidOperationException("Failed to assume role with AWS STS.", ex);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new InvalidOperationException("Invalid region endpoint or missing credentials.", ex);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("An unexpected error occurred while initializing the X-Ray client.", ex);
                    }
                }
            }

            return _sessionCredentials;
        }
        public async Task<TracesResult> GetTraces(GetTraceSummariesRequest req, ILogger log)
        {
            if (_xrayClient != null)
            {
                var resp = await _xrayClient.GetTraceSummariesAsync(req);

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
                    
            }
            else
            {
                log.LogWarning("Skip XRay API polling - client not initialized");
            }

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
            log.LogInformation("GetTraceSummaries@" + req.StartTime + " - " + req.EndTime);

            return await GetTraces(reqObj, log);
        }
      

        async Task<TraceDetailsResult> GetTraceDetails(BatchGetTracesRequest req, ILogger log)
        {
            BatchGetTracesResponse resp = await _xrayClient.BatchGetTracesAsync(req);
            
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
                log.LogDebug(tracesJson);

                var jsonDoc = JsonDocument.Parse(tracesJson);

                var conv = new XRay2OTLP.Convert(null);
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
            var pollingInterval = -1 * context.GetInput<uint>();

            var getTraces = new TracesRequest()
            {
                StartTime = currentTime.AddSeconds(pollingInterval),
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

        //Do not use timer triggered functions to avoid overlap issues: https://stackoverflow.com/a/62640692
        //[FunctionName(nameof(ScheduledStart))]
        //public async Task ScheduledStart([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, [DurableClient] IDurableOrchestrationClient starter, ILogger log)
        //{
        //    await Execute(starter, log);
        //}
        //async Task<string> Execute(IDurableOrchestrationClient starter, ILogger log)
        //{
        //    string instanceId = await starter.StartNewAsync(nameof(RetrieveRecentTraces), null);
        //    log.LogInformation("Started orchestration with ID = '{instanceId}'.", instanceId);
        //    return instanceId;
        //}
        //[FunctionName(nameof(TestStart))]
        //public async Task<HttpResponseMessage> TestStart(
        //[HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestMessage req,
        //[DurableClient] IDurableOrchestrationClient starter,
        //ILogger log)
        //{
        //    log.LogWarning("TestStart");
        //    string instanceId = await Execute(starter, log);
        //    return starter.CreateCheckStatusResponse(req, instanceId);
        //}
        //...
        //..
        //.
        // Instead kick-off a timer...
        [FunctionName(nameof(TriggerPeriodicAPIPoller))]
        public async Task<HttpResponseMessage> TriggerPeriodicAPIPoller(
            [HttpTrigger(AuthorizationLevel.Admin, "POST")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {
            log.LogInformation("TriggerPeriodicAPIPoller");

            string instanceId = PeriodicAPIPollerSingletoninstanceId; 

            try
            {
                await client.StartNewAsync(nameof(PeriodicAPIPoller), instanceId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unable to start 'PeriodicAPIPoller'");

                var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                res.Content = new StringContent("{status:\"Failed\"}", null, "application/json");
            }

            try
            {
                return client.CreateCheckStatusResponse(req, instanceId);
            }
            catch(Exception ex) 
            {
                //CreateCheckStatusResponse doesn't work when deployed on K8s, as it cannot resovle the webhook from the env-var
                //https://github.com/Azure/azure-functions-host/issues/9024
                log.LogWarning("Unable to execute 'CreateCheckStatusResponse': "+ex.Message);
                    
                var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                res.Content = new StringContent("{status:\"OK\"}", null, "application/json");

                return res;
            }

        }

        [FunctionName(nameof(PeriodicAPIPoller))]
        public static async Task PeriodicAPIPoller(
            [OrchestrationTrigger] IDurableOrchestrationContext context, 
            ILogger log)
        {
            uint pollingIntervalSeconds;
            if (!UInt32.TryParse(Environment.GetEnvironmentVariable(PollingIntervalSeconds), out pollingIntervalSeconds))
            {
                if (UInt32.TryParse(Environment.GetEnvironmentVariable(PollingIntervalMinutes), out uint pollingIntervalMinutes))
                {
                    pollingIntervalSeconds = pollingIntervalMinutes * 60;
                }
                else
                {
                    pollingIntervalSeconds = 180;
                    log.LogWarning("Unable to parse PollingIntervalSeconds, using default value (180sec)");
                }
            }

            log.LogInformation("PeriodicAPIPoller @" + pollingIntervalSeconds + "s");

            var identityKey = Environment.GetEnvironmentVariable(AWSIdentityKey);
            await context.CallSubOrchestratorAsync(nameof(RetrieveRecentTraces), pollingIntervalSeconds);
            
            // sleep for x minutes before next poll
            DateTime nextCleanup = context.CurrentUtcDateTime.AddSeconds(pollingIntervalSeconds);
            await context.CreateTimer(nextCleanup, CancellationToken.None);

            context.ContinueAsNew(null);
        }

        //Due to a bug to get admin urls from CreateAndCheckResponse, add a dedicated function to terminate orchestration.
        [FunctionName(nameof(TerminatePeriodicAPIPoller))]
        public async Task<HttpResponseMessage> TerminatePeriodicAPIPoller(
        [HttpTrigger(AuthorizationLevel.Admin, "post")] HttpRequestMessage req,
        [DurableClient] IDurableOrchestrationClient client, ILogger log)
        {
            try
            {
                log.LogInformation("TerminatePeriodicAPIPoller");

                await client.TerminateAsync(PeriodicAPIPollerSingletoninstanceId, "Manually aborted");

                var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                res.Content = new StringContent("{status:\"Success\"}", null, "application/json");
                return res;
            }
            catch(Exception ex)
            {
                log.LogError(ex, "Failed to terminate '" + PeriodicAPIPollerSingletoninstanceId + "'");

                var res = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                res.Content = new StringContent("{status:\"Failed\"}", null, "application/json");
                return res;
            }
        }

        [FunctionName(nameof(TestPing))]
        public Task<HttpResponseMessage> TestPing(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestMessage req,
            ILogger log)
        {
            log.LogInformation(nameof(TestPing));

            var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            res.Content = new StringContent("{status:\"ping successful\"}", null, "application/json");
            return Task.FromResult(res);
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
            var resp = await _xrayClient.PutTraceSegmentsAsync(seg);
            
            var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            res.Content = new StringContent("{status:\"ok\"}", null, "application/json");

            return res;
        }

      

      
#endif
        #endregion

    }
}