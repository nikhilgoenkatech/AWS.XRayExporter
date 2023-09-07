using System;
using System.Collections.Generic;
using System.Text;

namespace OpenTelemetry
{
    internal static class AWSSemanticConventions
    {

        public const string AttributeAWSServiceName = "aws.service";
        public const string AttributeAWSOperationName = "aws.operation";
        public const string AttributeAWSRegion = "aws.region";
        public const string AttributeAWSRequestId = "aws.requestId";

        public const string AttributeAWSDynamoTableName = "aws.table_name";
        public const string AttributeAWSSQSQueueUrl = "aws.queue_url";

        public const string AttributeHttpStatusCode = "http.status_code";
        public const string AttributeHttpResponseContentLength = "http.response_content_length";

        public const string AttributeValueDynamoDb = "dynamodb";
        public const string AWSOperationAttribute = "aws.operation";
        public const string AWSAccountAttribute   = "aws.account_id";
        public const string AWSRegionAttribute    = "aws.region";
        public const string AWSRequestIDAttribute = "aws.request_id";
        // Currently different instrumentation uses different tag formats.
        // TODO(anuraaga): Find current instrumentation and consolidate.
        public const string AWSRequestIDAttribute2 = "aws.requestId";
        public const string AWSQueueURLAttribute   = "aws.queue_url";
        public const string AWSQueueURLAttribute2  = "aws.queue.url";
        public const string AWSServiceAttribute    = "aws.service";
        public const string AWSTableNameAttribute  = "aws.table_name";
        public const string AWSTableNameAttribute2 = "aws.table.name";
        // AWSXRayInProgressAttribute is the `in_progress` flag in an X-Ray segment
        public const string AWSXRayInProgressAttribute = "aws.xray.inprogress";
        // AWSXRayXForwardedForAttribute is the `x_forwarded_for` flag in an X-Ray segment
        public const string AWSXRayXForwardedForAttribute = "aws.xray.x_forwarded_for";
        // AWSXRayResourceARNAttribute is the `resource_arn` field in an X-Ray segment
        public const string AWSXRayResourceARNAttribute = "aws.xray.resource_arn";
        // AWSXRayTracedAttribute is the `traced` field in an X-Ray subsegment
        public const string AWSXRayTracedAttribute = "aws.xray.traced";
        // AWSXraySegmentAnnotationsAttribute is the attribute that
        // will be treated by the X-Ray exporter as the annotation keys.
        public const string AWSXraySegmentAnnotationsAttribute = "aws.xray.annotations";
        // AWSXraySegmentMetadataAttributePrefix is the prefix of the attribute that
        // will be treated by the X-Ray exporter as metadata. The key of a metadata
        // will be AWSXraySegmentMetadataAttributePrefix + <metadata_key>.
        public const string AWSXraySegmentMetadataAttributePrefix = "aws.xray.metadata.";
        // AWSXrayRetriesAttribute is the `retries` field in an X-Ray (sub)segment.
        public const string AWSXrayRetriesAttribute = "aws.xray.retries";
        // AWSXrayExceptionIDAttribute is the `id` field in an exception
        public const string AWSXrayExceptionIDAttribute = "aws.xray.exception.id";
        // AWSXrayExceptionRemoteAttribute is the `remote` field in an exception
        public const string AWSXrayExceptionRemoteAttribute = "aws.xray.exception.remote";
        // AWSXrayExceptionTruncatedAttribute is the `truncated` field in an exception
        public const string AWSXrayExceptionTruncatedAttribute = "aws.xray.exception.truncated";
        // AWSXrayExceptionSkippedAttribute is the `skipped` field in an exception
        public const string AWSXrayExceptionSkippedAttribute = "aws.xray.exception.skipped";
        // AWSXrayExceptionCauseAttribute is the `cause` field in an exception
        public const string AWSXrayExceptionCauseAttribute = "aws.xray.exception.cause";

        //XRay Segment Reference
        public const string AWSXRayTraceIdAttribute = "aws.xray.trace_id";
        public const string AWSXRaySegmentIdAttribute = "aws.xray.segment_id";
    }
}
