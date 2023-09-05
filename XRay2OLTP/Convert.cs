using System;
using Google.Protobuf;
using Opentelemetry.Proto.Trace.V1;
using Opentelemetry.Proto.Common.V1;
using Opentelemetry.Proto.Resource.V1;
using System.Text.Json;
using Opentelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OTelSemConv = OpenTelemetry.SemanticConventions;
using ResSemConv = OpenTelemetry.ResourceSemanticConventions;
using XRay;

namespace XRay2OTLP
{
    //
    //XRay SegmentMapping: https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/awsxrayreceiver/internal/translator/translator.go
    //
    public class Convert
    {
        private readonly ILogger _logger;

        public readonly bool _SimulateRealtime = false;

        public Convert(ILoggerFactory? loggerFactory)
        {
            _logger = loggerFactory?.CreateLogger<Convert>()
              ?? NullLoggerFactory.Instance.CreateLogger<Convert>();
        }
#if DEBUG
        public Convert(ILoggerFactory? loggerFactory,bool simulateRealtime): this(loggerFactory)
        {
            _SimulateRealtime = simulateRealtime;
        }

        internal int step = 0;
#endif

        public ulong ParseTimestampToNano(JsonElement e, string key)
        {
#if DEBUG
            if (_SimulateRealtime)
            {
                DateTimeOffset ts;
                if (key == Attributes.Start)
                {

                    ts = DateTime.UtcNow.AddMilliseconds(step * 50);
                    step++;
                }
                else
                    ts = DateTime.UtcNow.AddMilliseconds((step * 50) + 200);

                DateTime epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return (ulong)((ts - epochStart).Ticks*100);

            }
            else
#endif

                return (ulong)(DoubleValue(e, key)* 1000000000);
        }
        
        internal string ParseTraceId(string traceid)
        {
#if DEBUG
            if (_SimulateRealtime) //generate a unique trace-id per run
                return BitConverter.ToString(Guid.NewGuid().ToByteArray()).Replace("-", string.Empty).ToLower();
            else
#endif
                return traceid.Substring(2).Replace("-",String.Empty); //removing versions string and separators
        }

        internal string ParseSpanId(string segmentid)
        {
            return segmentid;
        }

        public ulong ConvertTimeStampToNano(DateTime dt)
        {
            return (ulong)(dt.Ticks) * 100;
        }
      

        public bool TryAddResourceAttribute(ResourceSpans s, string key, int? value)
        {
            if (value.HasValue)
            {
                s.Resource.Attributes.Add(new KeyValue()
                {
                    Key = key,
                    Value = new AnyValue()
                    {
                        IntValue = value.Value
                    }
                }) ;

                return true;
            }
            return false;
        }

        public bool TryAddResourceAttribute(ResourceSpans s, string key, string value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                s.Resource.Attributes.Add(new KeyValue()
                {
                    Key = key,
                    Value = new AnyValue()
                    {
                        StringValue = value
                    }
                });

                return true;
            }
            return false;
        }

        public bool TryAddAttribute(Span s, string key, string value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                s.Attributes.Add(new KeyValue()
                {
                    Key = key,
                    Value = new AnyValue()
                    {
                        StringValue = value
                    }
                }); ;
                return true;
            }
            return false;

        }

        public bool TryAddAttribute(Span s, string key, int? value)
        {
            if (value.HasValue)
            {
                s.Attributes.Add(new KeyValue()
                {
                    Key = key,
                    Value = new AnyValue()
                    {
                        IntValue = value.Value
                    }
                }); ;
                return true;
            }
            return false;
        }


        internal ByteString ConvertToByteString(string str)
        {
            byte[] byteArray = new byte[str.Length / 2];
            for (int i = 0; i < byteArray.Length; i++)
            {
                byteArray[i] = System.Convert.ToByte(str.Substring(i * 2, 2), 16);
            }

            ByteString byteStr = ByteString.CopyFrom(byteArray);

            return byteStr;
        }

        
        internal double DoubleValue(JsonElement e, string key)
        {
            JsonElement val;
            if (e.TryGetProperty(key, out val))
            {
                double r;
                if (val.TryGetDouble(out r))
                    return r;
                else
                    _logger.LogDebug("Invalid value, which is not a double'" + key + "'");

            }
            else
            {
                _logger.LogDebug("Missing property '" + key + "'");
            }

            return 0d;

        }

        internal bool BoolValue(JsonElement e, string key)
        {
            JsonElement val;
            if (e.TryGetProperty(key, out val))
            {
                return val.GetBoolean();
            }
            else
            {
                _logger.LogDebug("Missing property '" + key + "'");
            }

            return false;

        }

        internal string Value(JsonElement e, string key)
        {
            JsonElement val;
            if (e.TryGetProperty(key, out val))
            {
                var r = val.GetString();
                if (!String.IsNullOrEmpty(r))
                    return r;
            }
            else
            {
                _logger.LogDebug("Missing property '" + key + "'");
            }

            return "";

        }
        internal int? NumberValue(JsonElement e, string key)
        {
            JsonElement val;
            if (e.TryGetProperty(key, out val))
            {
                return val.GetInt32();
            }
            else
            {
                _logger.LogDebug("Missing property '" + key + "'");
            }

            return null;

        }


        public void addAWSToRessource(ResourceSpans resSpan, JsonElement segment)
        {
            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudProvider, CloudProviderConstants.AWS);
                TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudAccount, Value(segment, AwsAttributes.AccountID));

                JsonElement elem;
                if (aws.TryGetProperty(AwsAttributes.EC2, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudZone, Value(elem, AwsAttributes.EC2AvailabilityZone));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeHostId, Value(elem, AwsAttributes.EC2InstanceId));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeHostType, Value(elem, AwsAttributes.EC2InstanceSize));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeHostImageId, Value(elem, AwsAttributes.EC2AmiId));
                }

                if (aws.TryGetProperty(AwsAttributes.ECS, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeContainerName, Value(elem, AwsAttributes.ECSContainername));
                    TryAddResourceAttribute(resSpan, ContainerSemanticConventions.AttributeContainerId, Value(elem, AwsAttributes.ECSContainerId));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudZone, Value(elem, AwsAttributes.ECSAvailabilityZone));
                    
                }

                if (aws.TryGetProperty(AwsAttributes.EKS, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeK8sCluster, Value(elem, AwsAttributes.EKSClusterName));
                    TryAddResourceAttribute(resSpan, ContainerSemanticConventions.AttributeContainerId, Value(elem, AwsAttributes.EKSContainerId));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeK8sPod, Value(elem, AwsAttributes.EKSPod));

                }
                
                if (aws.TryGetProperty(AwsAttributes.Beanstalk, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceNamespace, Value(elem, AwsAttributes.BeanstalkEnvironment));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceInstance, NumberValue(elem, AwsAttributes.BeanstalkDeploymentId));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceVersion, Value(elem, AwsAttributes.BeanstalkVersionLabel));

                }

            }

        }

        public void addAWSToInstrumentationLibrary(InstrumentationLibrarySpans libSpan, JsonElement segment)
        {
            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                JsonElement elem;
                if (aws.TryGetProperty(AwsAttributes.XRay, out elem))
                {
                    var sdk = Value(elem, AwsAttributes.XRaySDK);
                    if (BoolValue(elem, AwsAttributes.XRayAutoInstr))
                        sdk += " with auto-instrumentation";
                    libSpan.InstrumentationLibrary = new InstrumentationLibrary()
                    {
                        Name = sdk??"XRay",
                        Version = Value(elem, AwsAttributes.XRayVersion)
                };
                }

            }

        }

        public void addAWSToSpan(Span span, JsonElement segment)
        {
            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                TryAddAttribute(span, AWSSemanticConventions.AWSAccountAttribute, Value(aws, AwsAttributes.AccountID));
                TryAddAttribute(span, AWSSemanticConventions.AWSOperationAttribute, Value(aws, AwsAttributes.Operation));
                TryAddAttribute(span, AWSSemanticConventions.AWSRegionAttribute, Value(aws, AwsAttributes.RemoteRegion));
                TryAddAttribute(span, AWSSemanticConventions.AWSRequestIDAttribute, Value(aws, AwsAttributes.RequestID));
                TryAddAttribute(span, AWSSemanticConventions.AWSQueueURLAttribute, Value(aws, AwsAttributes.QueueURL));
                TryAddAttribute(span, AWSSemanticConventions.AWSTableNameAttribute, Value(aws, AwsAttributes.TableName));
                TryAddAttribute(span, AWSSemanticConventions.AWSXrayRetriesAttribute, NumberValue(aws, AwsAttributes.Retries));
            }

        }

        public Status.Types.StatusCode SpanStatusFromHttpStatus(int statusCode)
        {
            if (statusCode >= 100 && statusCode < 399)
                return Status.Types.StatusCode.Unset;
            else
                return Status.Types.StatusCode.Error;
        }

        public void addHttp(Span span, JsonElement segment)
        {
            JsonElement http;
            if (segment.TryGetProperty(Properties.Http, out http))
            {
                JsonElement elem;

                if (http.TryGetProperty(HttpAttributes.Request, out elem))
                {
                    TryAddAttribute(span, OTelSemConv.AttributeHttpMethod, Value(elem, HttpAttributes.Method));

                    if (TryAddAttribute(span, OTelSemConv.AttributeHttpClientIP, Value(elem, HttpAttributes.ClientIp)))
                        span.Kind = Span.Types.SpanKind.Server; 

                    TryAddAttribute(span, OTelSemConv.AttributeHttpUserAgent, Value(elem, HttpAttributes.UserAgent));
                    TryAddAttribute(span, OTelSemConv.AttributeHttpUrl, Value(elem, HttpAttributes.Url));
                    //TryAddAttribute(span, OTelSemConv.AttributeHttp, Value(elem, HttpAttributes.XForwardFor));
                }

                if (http.TryGetProperty(HttpAttributes.Response, out elem))
                {
                    var val = NumberValue(elem, HttpAttributes.Status);
                    //TODO
                    //if (TryAddAttribute(span, OTelSemConv.AttributeHttpStatusCode., val))
                    //  span.Status = SpanStatusFromHttpStatus(val);

                    TryAddAttribute(span, OTelSemConv.AttributeHttpResponseContentLength, NumberValue(elem, HttpAttributes.ContentLength));
                }
                
            }

        }

        public void addSql(Span span, JsonElement segment)
        {
            JsonElement elem;
            if (segment.TryGetProperty(Properties.Sql, out elem))
            {
                // https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/c615d2db351929b99e46f7b427f39c12afe15b54/exporter/awsxrayexporter/translator/sql.go#L60
                var sqlUrl = Value(elem, SqlAttributes.Url);
                if (!String.IsNullOrEmpty(sqlUrl))
                {
                    var val = sqlUrl.Split(new char[] { '/' });
                    if (val.Length == 2)
                    {
                        TryAddAttribute(span, OTelSemConv.AttributeDbUrl, val[0]);
                        TryAddAttribute(span, OTelSemConv.AttributeDbInstance, val[1]);
                    }
                }
                TryAddAttribute(span, OTelSemConv.AttributeDbSystem, Value(elem, SqlAttributes.DatabaseType));
                TryAddAttribute(span, OTelSemConv.AttributeDbStatement, Value(elem, SqlAttributes.SanitizedQuery));
                TryAddAttribute(span, OTelSemConv.AttributeDbUser, Value(elem, SqlAttributes.User));

            }

        }

        public void AddSpansFromSegment(string traceId, string parentSpanId, JsonElement segment, ExportTraceServiceRequest export)
        {
            var resSpan = new ResourceSpans();
            export.ResourceSpans.Add(resSpan);

            if (String.IsNullOrEmpty(traceId)) 
                traceId = ParseTraceId(Value(segment, Attributes.TraceId));

            var spanId = ParseSpanId(Value(segment, Attributes.SegmentId));
            var parentId = ParseSpanId(Value(segment, Attributes.ParentId));
            if (String.IsNullOrEmpty(parentId) && !String.IsNullOrEmpty(parentSpanId)) 
                    parentId = parentSpanId;

            resSpan.Resource = new Resource();

            TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceName, Value(segment, Attributes.Name));
            addAWSToRessource(resSpan, segment);

            var libSpan = new InstrumentationLibrarySpans();
            addAWSToInstrumentationLibrary(libSpan, segment);

            resSpan.InstrumentationLibrarySpans.Add(libSpan);

            var span = new Span();
            libSpan.Spans.Add(span);

            span.TraceId = ConvertToByteString(traceId);
            span.SpanId = ConvertToByteString(spanId);
            if (!String.IsNullOrEmpty(parentId))
                span.ParentSpanId = ConvertToByteString(parentId);
            else
                span.Kind = Span.Types.SpanKind.Server;

            span.Name = Value(segment, Attributes.Name);

            span.StartTimeUnixNano = ParseTimestampToNano(segment,Attributes.Start);
            span.EndTimeUnixNano = ParseTimestampToNano(segment, Attributes.End);

            addAWSToSpan(span, segment);
            addHttp(span, segment);
            addSql(span, segment);

            JsonElement elem;
            if (span.Kind == Span.Types.SpanKind.Unspecified && !segment.TryGetProperty(Attributes.Namespace, out elem))
                span.Kind = Span.Types.SpanKind.Internal;

            if (!String.IsNullOrEmpty(parentSpanId))
                span.Kind = Span.Types.SpanKind.Client;

            JsonElement subSegments;
            if (segment.TryGetProperty(Properties.Subsegments, out subSegments))
            {
                var subs= subSegments.EnumerateArray();
                while (subs.MoveNext())
                {
                    AddSpansFromSegment(traceId, spanId, subs.Current, export);
                }
            }
        }
        public ExportTraceServiceRequest FromXRay(string xrayJsonStr)
        {
            _logger.LogDebug(xrayJsonStr);
            var root = JsonDocument.Parse(xrayJsonStr);

            return FromXRay(root);
        }

        public ExportTraceServiceRequest FromXRaySegmentDocArray(JsonDocument root)
        {
            var export = new ExportTraceServiceRequest();

            var s = root.RootElement.EnumerateArray();
            while (s.MoveNext())
            {
                AddSpansFromSegment(String.Empty, String.Empty, s.Current, export);
            }

            return export;
        }
    

        //parsing response from batchgettraces
        public ExportTraceServiceRequest FromXRay(JsonDocument root)
        {

            var export = new ExportTraceServiceRequest();
            
            var t = root.RootElement.GetProperty(Traces.Root).EnumerateArray();
            while (t.MoveNext())
            {
                var traceId = ParseTraceId(Value(t.Current, Traces.Id));

                var s = t.Current.GetProperty(Traces.Segments).EnumerateArray();
                
                while (s.MoveNext())
                {
                    var segmentJsonStr = s.Current.GetProperty(Traces.Document).GetString();
                    if (!String.IsNullOrEmpty(segmentJsonStr))
                    {
                        var segmentDoc = JsonDocument.Parse(segmentJsonStr);

                        AddSpansFromSegment(traceId, String.Empty, segmentDoc.RootElement, export);
                    }
                }

             }

            return export;
        }
    }
}