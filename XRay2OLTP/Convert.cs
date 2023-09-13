using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opentelemetry.Proto.Collector.Trace.V1;
using Opentelemetry.Proto.Common.V1;
using Opentelemetry.Proto.Resource.V1;
using Opentelemetry.Proto.Trace.V1;
using OpenTelemetry;
using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using XRay;
using OTelSemConv = OpenTelemetry.SemanticConventions;
using ResSemConv = OpenTelemetry.ResourceSemanticConventions;

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
        public Convert(ILoggerFactory? loggerFactory, bool simulateRealtime) : this(loggerFactory)
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
                return (ulong)((ts - epochStart).Ticks * 100);

            }
            else
#endif

                return (ulong)(DoubleValue(e, key) * 1000000000);
        }

        internal string ParseTraceId(string traceid)
        {
#if DEBUG
            if (_SimulateRealtime) //generate a unique trace-id per run
                return BitConverter.ToString(Guid.NewGuid().ToByteArray()).Replace("-", string.Empty).ToLower();
            else
#endif
                return traceid.Substring(2).Replace("-", String.Empty); //removing versions string and separators
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
                });

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


        public void AddAWSToRessource(ResourceSpans resSpan, JsonElement segment)
        {
            TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudProvider, CloudProviderConstants.AWS);

            TryAddResourceAttribute(resSpan, CloudProviderSemanticConventions.AttributeResourceID, Value(segment, Properties.Arn));
            

            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudAccount, Value(aws, AwsAttributes.AccountID));
                TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudRegion, Value(aws, AwsAttributes.RemoteRegion));

                if(TryAddResourceAttribute(resSpan, ResSemConv.AttributeFaasName, Value(aws, AwsAttributes.LambdaName)))
                {
                    //extract arn from request url
                    JsonElement http;
                    if (segment.TryGetProperty(Properties.Http, out http))
                    {
                        JsonElement req;
                        if (http.TryGetProperty(HttpAttributes.Request, out req))
                        {
                            JsonElement url;
                            if (req.TryGetProperty(HttpAttributes.Url, out url))
                            {
                                Regex regex = new Regex(@".*(arn:.*)");
                                var match = regex.Match(url.GetString());
                                if (match.Success)
                                    TryAddResourceAttribute(resSpan, CloudProviderSemanticConventions.AttributeResourceID, match.Groups[1].Value);
                            }
                        }
                    }
                    
                }

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

                if (aws.TryGetProperty(AwsAttributes.ApiGateway, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudAccount, Value(elem, AwsAttributes.AccountID));
                }

            }

        }

        public void AddAWSToInstrumentationLibrary(InstrumentationLibrarySpans libSpan, JsonElement segment)
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
                        Name = sdk ?? "XRay",
                        Version = Value(elem, AwsAttributes.XRayVersion)
                    };
                }

            }

        }

        public void AddAWSToSpan(Span span, JsonElement segment)
        {
            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                TryAddAttribute(span, AWSSemanticConventions.AWSOperationAttribute, Value(aws, AwsAttributes.Operation));
                TryAddAttribute(span, AWSSemanticConventions.AWSRequestIDAttribute, Value(aws, AwsAttributes.RequestID));
                TryAddAttribute(span, AWSSemanticConventions.AWSQueueURLAttribute, Value(aws, AwsAttributes.QueueURL));
                TryAddAttribute(span, AWSSemanticConventions.AWSTableNameAttribute, Value(aws, AwsAttributes.TableName));
                TryAddAttribute(span, AWSSemanticConventions.AWSXrayRetriesAttribute, NumberValue(aws, AwsAttributes.Retries));


                JsonElement sub;
                if (aws.TryGetProperty(AwsAttributes.ApiGateway, out sub))
                {
                    TryAddAttribute(span, AWSSemanticConventions.AWSRequestIDAttribute, Value(sub, AwsAttributes.RequestID));
                    TryAddAttribute(span, AWSSemanticConventions.AWSApiIDAttribute, Value(sub, AwsAttributes.ApiID));
                    TryAddAttribute(span, AWSSemanticConventions.AWSApiStageAttribute, Value(sub, AwsAttributes.ApiStage));
                }

            }

        }

        public void SetSpanStatusFromHttpStatus(Span s, int? statusCode)
        {
            if (statusCode.HasValue)
            {
                if (statusCode >= 100 && statusCode < 399)
                    s.Status = new Status() { Code = Status.Types.StatusCode.Unset };
                else
                    s.Status = new Status() { Code = Status.Types.StatusCode.Error };
            }
        }
        

        public void AddHttp(Span span, JsonElement segment)
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
                    TryAddAttribute(span, OTelSemConv.AttributeHttpMethod, Value(elem, HttpAttributes.Method));
                    TryAddAttribute(span, OTelSemConv.AttributeHttpClientIP, Value(elem, HttpAttributes.ClientIp));
                    //TryAddAttribute(span, OTelSemConv.AttributeHttp, Value(elem, HttpAttributes.XForwardFor));
                }

                if (http.TryGetProperty(HttpAttributes.Response, out elem))
                {
                    var val = NumberValue(elem, HttpAttributes.Status);
                    TryAddAttribute(span, OTelSemConv.AttributeHttpStatusCode, val);
                    SetSpanStatusFromHttpStatus(span, val);

                    TryAddAttribute(span, OTelSemConv.AttributeHttpResponseContentLength, NumberValue(elem, HttpAttributes.ContentLength));
                }

            }

        }

        public void AddSql(Span span, JsonElement segment)
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

        public void AddXRayTraceContext(Span s, string XRayTraceId, string XRaySegementId)
        {
            s.Attributes.Add(new KeyValue()
            {
                Key = AWSSemanticConventions.AWSXRayTraceIdAttribute,
                Value = new AnyValue()
                {
                    StringValue = XRayTraceId
                }
            });

            s.Attributes.Add(new KeyValue()
            {
                Key = AWSSemanticConventions.AWSXRaySegmentIdAttribute,
                Value = new AnyValue()
                {
                    StringValue = XRaySegementId
                }
            }); 
        }
        public void AddSpansFromSegment(string xrayTraceId, string xrayParentSpanId, string origin, JsonElement segment, ExportTraceServiceRequest export)
        {
            var resSpan = new ResourceSpans();
            export.ResourceSpans.Add(resSpan);

            if (String.IsNullOrEmpty(xrayTraceId)) 
                xrayTraceId = Value(segment, Attributes.TraceId);

            if (String.IsNullOrEmpty(xrayParentSpanId))
                xrayParentSpanId = Value(segment, Attributes.ParentId);

            var xraySpanId = Value(segment, Attributes.SegmentId);

            resSpan.Resource = new Resource();


            TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceName, Value(segment, Attributes.Name));

            AddAWSToRessource(resSpan, segment);

            var libSpan = new InstrumentationLibrarySpans();
            AddAWSToInstrumentationLibrary(libSpan, segment);

            resSpan.InstrumentationLibrarySpans.Add(libSpan);

            var span = new Span();
            libSpan.Spans.Add(span);

            span.TraceId = ConvertToByteString(ParseTraceId(xrayTraceId));
            span.SpanId = ConvertToByteString(ParseSpanId(xraySpanId));
            if (!String.IsNullOrEmpty(xrayParentSpanId))
                span.ParentSpanId = ConvertToByteString(ParseSpanId(xrayParentSpanId));
            else
                span.Kind = Span.Types.SpanKind.Server;

            AddXRayTraceContext(span, xrayTraceId, xraySpanId);

            span.Name = Value(segment, Attributes.Name);

            span.StartTimeUnixNano = ParseTimestampToNano(segment,Attributes.Start);
            span.EndTimeUnixNano = ParseTimestampToNano(segment, Attributes.End);

            if (String.IsNullOrEmpty(origin))
                origin = Value(segment, Properties.Origin);
            TryAddAttribute(span, AWSSemanticConventions.AWSXRaySegmentOriginAttribute, origin);

            AddAWSToSpan(span, segment);
            AddHttp(span, segment);
            AddSql(span, segment);

            JsonElement elem;
            if (span.Kind == Span.Types.SpanKind.Unspecified && !segment.TryGetProperty(Attributes.Namespace, out elem))
                span.Kind = Span.Types.SpanKind.Internal;

            if (!String.IsNullOrEmpty(xrayParentSpanId))
                span.Kind = Span.Types.SpanKind.Client;

            JsonElement subSegments;
            if (segment.TryGetProperty(Properties.Subsegments, out subSegments))
            {
                var subs= subSegments.EnumerateArray();
                while (subs.MoveNext())
                {
                    AddSpansFromSegment(xrayTraceId, xraySpanId, origin, subs.Current, export);
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
                AddSpansFromSegment(String.Empty, String.Empty, String.Empty, s.Current, export);
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

                        AddSpansFromSegment(traceId, String.Empty, String.Empty, segmentDoc.RootElement, export);
                    }
                }

             }

            return export;
        }
    }
}