using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opentelemetry.Proto.Collector.Trace.V1;
using Opentelemetry.Proto.Common.V1;
using Opentelemetry.Proto.Resource.V1;
using Opentelemetry.Proto.Trace.V1;
using OpenTelemetry;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using XRay;
using OTelSemConv = OpenTelemetry.SemanticConventions;
using ResSemConv = OpenTelemetry.ResourceSemanticConventions;

namespace XRay2OTLP
{
    //
    //XRay SegmentMapping: https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/main/receiver/awsxrayreceiver/internal/translator/translator.go
    //
    //Sample Segments:
    //https://docs.aws.amazon.com/xray/latest/devguide/xray-api-segmentdocuments.html
    public class Convert
    {
        private readonly ILogger _logger;

#if DEBUG
        internal bool _SimulateRealtime = false;
        internal bool _SimulateTraceId = false;
        internal int _SpanSequenceStep = 0;
#endif
        public Convert(ILoggerFactory? loggerFactory)
        {
            _logger = loggerFactory?.CreateLogger<Convert>()
              ?? NullLoggerFactory.Instance.CreateLogger<Convert>();
        }

        public Convert(ILoggerFactory? loggerFactory, bool simulateRealtime, bool simulateTraceId) : this(loggerFactory)
        {
#if DEBUG
            _SimulateRealtime = simulateRealtime;
            _SimulateTraceId = simulateTraceId;
#else
    #warning "Do not use this constructor in RELEASE. Debugging features will be disabled!"
#endif
        }

        public ulong ParseTimestampToNano(JsonElement e, string key)
        {
#if DEBUG
            if (_SimulateRealtime)
            {
                DateTimeOffset ts;
                if (key == Attributes.Start)
                {

                    ts = DateTime.UtcNow.AddMilliseconds(_SpanSequenceStep * 50);
                    _SpanSequenceStep++;
                }
                else
                    ts = DateTime.UtcNow.AddMilliseconds((_SpanSequenceStep * 50) + 200);

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
            if (_SimulateTraceId) //generate a unique trace-id per run
            {
                traceid = BitConverter.ToString(Guid.NewGuid().ToByteArray()).Replace("-", string.Empty).ToLower();
                Debug.WriteLine("[XRay2OTLP] Generated Trace-Id: " + traceid);
                return traceid;
            }
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

        internal string Value(JsonElement e, string key, string defaultValue = "")
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

            return defaultValue;

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

        private Regex queueUrlPattern = new Regex(@"https://sqs\.(?<region>[^.]+)\.amazonaws\.com/(?<accountId>\d+)/(?<resource>.+)", RegexOptions.Compiled);
        private Regex arnPattern= new Regex(@"arn:aws:[^:]+:(?<region>[^:]*):(?<accountId>[^:]*):(?<resource>.+)", RegexOptions.Compiled);

        private bool MatchAccountRegionResource(Match match, out string accountId, out string region, out string resource)
        {
            if (match.Success)
            {
                accountId = match.Groups["accountId"].Value;
                region = match.Groups["region"].Value;
                resource = match.Groups["resource"].Value;

                return true;
            }
            else
            {
                accountId = region = resource = String.Empty;
                return false;
            }
        }
        private bool ParseArn(string arn, out string accountId, out string region, out string resource)
        {
            Match match = arnPattern.Match(arn);
            return MatchAccountRegionResource(match, out accountId, out region, out resource);
        }

        private bool ParseQueueUrl(string queueurl, out string accountId, out string region, out string resource)
        {
            Match match = queueUrlPattern.Match(queueurl);
            return MatchAccountRegionResource(match, out accountId, out region, out resource);

        }

        public string AddAWS(ResourceSpans resSpan, Span span, JsonElement segment, out string relatedServiceName)
        {
            string spanName = String.Empty;

            string serviceName = Value(segment, Attributes.Name);
            bool isInternal = true;

            TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudProvider, CloudProviderConstants.AWS);
            TryAddResourceAttribute(resSpan, CloudProviderSemanticConventions.AttributeResourceID, Value(segment, Properties.Arn));

            JsonElement aws;
            if (segment.TryGetProperty(Properties.Aws, out aws))
            {
                TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudAccount, Value(aws, AwsAttributes.AccountID));
            
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
                    //TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceNamespace, Value(elem, AwsAttributes.BeanstalkEnvironment));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceInstance, NumberValue(elem, AwsAttributes.BeanstalkDeploymentId));
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceVersion, Value(elem, AwsAttributes.BeanstalkVersionLabel));
                }

                if (aws.TryGetProperty(AwsAttributes.ApiGateway, out elem))
                {
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeCloudAccount, Value(elem, AwsAttributes.AccountID));
                }

                //span attributes
             
                var prop = Value(aws, AwsAttributes.Operation);
                if (TryAddAttribute(span, OTelSemConv.AttributeRpcMethod, prop)) //former: TryAddAttribute(span, AWSSemanticConventions.AWSOperationAttribute, Value(aws, AwsAttributes.Operation));
                {
                    isInternal = false;
                    spanName = prop;
                    TryAddAttribute(span, OTelSemConv.AttributeRpcSystem, AWSSemanticConventions.RpcSystem);
                    TryAddAttribute(span, OTelSemConv.AttributeRpcService, Value(segment, Attributes.Name));
                }
                
                prop = Value(aws, AwsAttributes.TableName);
                if (TryAddAttribute(span, OTelSemConv.AttributeDbCollectionname, prop)) //former: TryAddAttribute(span, AWSSemanticConventions.AWSTableNameAttribute, Value(aws, AwsAttributes.TableName))
                {
                    TryAddAttribute(span, OTelSemConv.AttributeDbSystem, "dynamodb");

                    if (!String.IsNullOrEmpty(spanName))
                    {
                        serviceName = prop;
                        spanName += " " + prop;
                    }
                    prop = Value(aws, AwsAttributes.RemoteRegion);
                    if (!String.IsNullOrEmpty(prop))
                    {
                        serviceName += " in " + prop; 
                    }
                }

                prop = Value(aws, AwsAttributes.LambdaName);
                if (TryAddAttribute(span, ResSemConv.AttributeFaasName, prop))
                {
                    serviceName = prop;
                    if (!String.IsNullOrEmpty(spanName))
                        spanName += " " + prop; 
                }
                TryAddAttribute(span, AWSSemanticConventions.AWSFunctionArn, Value(aws, AwsAttributes.LambdaArn));

                prop = Value(aws, AwsAttributes.QueueURL);
                if (TryAddAttribute(span, AWSSemanticConventions.AWSQueueURLAttribute, prop))
                {
                    string arnAccountId, arnRegion, arnResource;
                    if (ParseQueueUrl(prop, out arnAccountId, out arnRegion, out arnResource))
                    {
                        serviceName = arnResource + " in " + arnRegion;
                        spanName += " " + arnResource;
                    }
                }

                prop = Value(aws, AwsAttributes.SNSArn);
                if (TryAddAttribute(span, AWSSemanticConventions.AWSTopicArn, prop))
                {
                    string arnAccountId, arnRegion, arnResource;
                    if (ParseArn(prop, out arnAccountId, out arnRegion, out arnResource))
                    {
                         serviceName = arnResource+" in "+arnRegion;
                         spanName += " " + arnResource;
                    }
                }

                JsonElement sub;
                if (aws.TryGetProperty(AwsAttributes.ApiGateway, out sub))
                {
                    TryAddAttribute(span, AWSSemanticConventions.AWSRequestIDAttribute, Value(sub, AwsAttributes.RequestID));
                    TryAddAttribute(span, AWSSemanticConventions.AWSApiIDAttribute, Value(sub, AwsAttributes.ApiID));
                    TryAddAttribute(span, AWSSemanticConventions.AWSApiStageAttribute, Value(sub, AwsAttributes.ApiStage));
                }

                TryAddAttribute(span, AWSSemanticConventions.AWSRequestIDAttribute, Value(aws, AwsAttributes.RequestID));
                TryAddAttribute(span, AWSSemanticConventions.AWSXrayRetriesAttribute, NumberValue(aws, AwsAttributes.Retries));


            }
            
            if (isInternal)
            {
                span.Kind = Span.Types.SpanKind.Internal;
            }
            
            relatedServiceName = serviceName; 

            return spanName;

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


      
        public string AddHttp(Span span, JsonElement segment)
        {
            string spanName = String.Empty;

            JsonElement http;
            if (segment.TryGetProperty(Properties.Http, out http))
            {
                JsonElement elem;
                 
                if (http.TryGetProperty(HttpAttributes.Request, out elem))
                {
                    TryAddAttribute(span, OTelSemConv.AttributeHttpClientIP, Value(elem, HttpAttributes.ClientIp));
                 
                    string httpMethod = Value(elem, HttpAttributes.Method, SemanticConventionsConstants.HttpMethodOther);
                    TryAddAttribute(span, OTelSemConv.AttributeHttpRequestMethod, httpMethod);
                    spanName = httpMethod;

                    string url = Value(elem, HttpAttributes.Url);
                    if (TryAddAttribute(span, OTelSemConv.AttributeHttpUrl, url))
                    {
                        Uri uri;
                        if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                        {
                            spanName += " "+uri.AbsolutePath;

                            TryAddAttribute(span, OTelSemConv.AttributeUrlScheme, uri.Scheme);
                            TryAddAttribute(span, OTelSemConv.AttributeServerAddress, uri.Host);
                            TryAddAttribute(span, OTelSemConv.AttributeServerPort, uri.Port);
                        }
                    }
                    TryAddAttribute(span, OTelSemConv.AttributeHttpClientIP, Value(elem, HttpAttributes.ClientIp));
                    TryAddAttribute(span, OTelSemConv.AttributeHttpUserAgent, Value(elem, HttpAttributes.UserAgent));
                }

                if (http.TryGetProperty(HttpAttributes.Response, out elem))
                {
                    var val = NumberValue(elem, HttpAttributes.Status);
                    TryAddAttribute(span, OTelSemConv.AttributeHttpStatusCode, val);
                    SetSpanStatusFromHttpStatus(span, val);

                    TryAddAttribute(span, OTelSemConv.AttributeHttpResponseContentLength, NumberValue(elem, HttpAttributes.ContentLength));
                }

            }

            return spanName;
        }

        public void AddSql(Span span, JsonElement segment)
        {
            JsonElement elem;
            if (segment.TryGetProperty(Properties.Sql, out elem))
            {
                span.Kind = Span.Types.SpanKind.Client;
   
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

        public void AddLinks(Span span, JsonElement segment)
        {
            JsonElement link;
            if (segment.TryGetProperty(Properties.Links, out link))
            {
                var links = link.EnumerateArray();
                while (links.MoveNext())
                {
                    JsonElement elem;

                    if (links.Current.TryGetProperty(LinkAttributes.Attributes, out elem))
                    {
                        if (Value(elem, LinkAttributes.Type) == LinkAttributes.TypeParent) //only process if it's a parent link
                        {
                            var refXrayTraceId = Value(links.Current, Attributes.TraceId);
                            var refXraySpanId = Value(links.Current, Attributes.SegmentId);

                            if (!String.IsNullOrEmpty(refXraySpanId) && !String.IsNullOrEmpty(refXrayTraceId))
                            {
                                var lnk = new Span.Types.Link()
                                {
                                    TraceId = ConvertToByteString(ParseTraceId(refXrayTraceId)),
                                    SpanId = ConvertToByteString(ParseSpanId(refXraySpanId))
                                };

                                lnk.Attributes.Add(new KeyValue()
                                {
                                    Key = AWSSemanticConventions.AWSXRayTraceIdAttribute,
                                    Value = new AnyValue()
                                    {
                                        StringValue = refXrayTraceId
                                    }
                                });

                                lnk.Attributes.Add(new KeyValue()
                                {
                                    Key = AWSSemanticConventions.AWSXRaySegmentIdAttribute,
                                    Value = new AnyValue()
                                    {
                                        StringValue = refXraySpanId
                                    }
                                });
                            }

                        }
                    }

                }
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
        public void AddSpansFromSegment(string xrayTraceId, string xrayParentSpanId, string origin, ResourceSpans? originResourceSpan, JsonElement segment, ExportTraceServiceRequest export)
        {
            ResourceSpans resSpan;
            if (originResourceSpan == null)
            {
                resSpan = new ResourceSpans();
                export.ResourceSpans.Add(resSpan);
                resSpan.Resource = new Resource();
            }
            else
                resSpan = originResourceSpan;

            if (String.IsNullOrEmpty(xrayTraceId)) 
                xrayTraceId = Value(segment, Attributes.TraceId);

            if (String.IsNullOrEmpty(xrayParentSpanId))
                xrayParentSpanId = Value(segment, Attributes.ParentId);

            var xraySpanId = Value(segment, Attributes.SegmentId);

            var libSpan = new InstrumentationLibrarySpans();
            AddAWSToInstrumentationLibrary(libSpan, segment);

            resSpan?.InstrumentationLibrarySpans.Add(libSpan);

            var span = new Span();
            libSpan.Spans.Add(span);

            span.TraceId = ConvertToByteString(ParseTraceId(xrayTraceId));
            span.SpanId = ConvertToByteString(ParseSpanId(xraySpanId));
            if (!String.IsNullOrEmpty(xrayParentSpanId))
                span.ParentSpanId = ConvertToByteString(ParseSpanId(xrayParentSpanId));
            else if (originResourceSpan != null)
                span.Kind = Span.Types.SpanKind.Client;
            else
                span.Kind = Span.Types.SpanKind.Server; //root 
            
            AddXRayTraceContext(span, xrayTraceId, xraySpanId);

            string spanName = string.Empty;  

            span.StartTimeUnixNano = ParseTimestampToNano(segment,Attributes.Start);
            span.EndTimeUnixNano = ParseTimestampToNano(segment, Attributes.End);

            AddLinks(span, segment);

          
#pragma warning disable CS8604
            string serviceName = string.Empty;
            string operationSpanName = AddAWS(resSpan, span, segment, out serviceName);
#pragma warning restore CS8604

            if (operationSpanName != String.Empty) spanName = operationSpanName;

            operationSpanName = AddHttp(span, segment);
            if (operationSpanName != String.Empty)
            {
                spanName = operationSpanName;
            }

            if (BoolValue(segment, Attributes.Error)) 
                span.Status = new Status() { Code = Status.Types.StatusCode.Error };


            AddSql(span, segment);

            if (String.IsNullOrEmpty(spanName))
                span.Name = Value(segment, Attributes.Name);
            else
                span.Name = spanName;

            if (String.IsNullOrEmpty(origin))
            {
                span.Kind = Span.Types.SpanKind.Server;

                string originProp = Value(segment, Properties.Origin);
                if (!String.IsNullOrEmpty(originProp))
                {
                    origin = originProp;
                    TryAddAttribute(span, AWSSemanticConventions.AWSXRaySegmentOriginAttribute, origin);
                    TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceNamespace, origin);
                      
                    if (!string.IsNullOrEmpty(serviceName))
                        TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceName, serviceName);
                    else
                        TryAddResourceAttribute(resSpan, ResSemConv.AttributeServiceName, origin);
                }
            }
            
            if (span.Kind == Span.Types.SpanKind.Unspecified)
            {
                span.Kind = Span.Types.SpanKind.Client;
            }
            
            JsonElement subSegments;
            if (segment.TryGetProperty(Properties.Subsegments, out subSegments))
            {
                var subs= subSegments.EnumerateArray();
                while (subs.MoveNext())
                {
                    AddSpansFromSegment(xrayTraceId, xraySpanId, origin, resSpan, subs.Current, export);
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
                AddSpansFromSegment(String.Empty, String.Empty, String.Empty, null, s.Current, export);
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

                        AddSpansFromSegment(traceId, String.Empty, String.Empty, null,segmentDoc.RootElement, export);
                    }
                }

             }

            return export;
        }
    }
}