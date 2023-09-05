using System;
using System.Collections.Generic;
using System.Text;

namespace XRay
{

    internal static class AwsAttributes
    {
        public const string Beanstalk = "elastic_beanstalk";
        public const string CWLogs = "cloudwatch_logs";
        public const string ECS = "ecs";
        public const string EC2 = "ec2";
        public const string EKS = "eks";
        public const string XRay = "xray";

        public const string AccountID = "account_id";
        public const string Operation = "operation";
        public const string RemoteRegion = "region";
        public const string RequestID = "request_id";
        public const string QueueURL = "queue_url";
        public const string TableName = "table_name";
        public const string TableNames = "table_names";
        public const string Retries = "retries";

        public const string EC2AvailabilityZone = "availability_zone";
        public const string EC2InstanceId = "instance_id";
        public const string EC2InstanceSize = "instance_size";
        public const string EC2AmiId = "ami_id";
        
        public const string ECSContainername = "container";
        public const string ECSAvailabilityZone = "availability_zone";
        public const string ECSContainerId = "container_id";
        public const string ECSTaskArn = "task_arn";
        public const string ECSTaskFamily = "task_family";
        public const string ECSClusterArn = "cluster_arn";
        public const string ECSContainerArn = "container_arn";
        public const string ECSLaunchType = "launch_type";

        public const string EKSContainerId = "container_id";
        public const string EKSClusterName = "cluster_name";
        public const string EKSPod = "pod";

        public const string BeanstalkEnvironment = "environment_name";
        public const string BeanstalkDeploymentId = "deployment_id";
        public const string BeanstalkVersionLabel = "version_label";

        public const string LogGroup = "log_group";
        public const string LogGroupArn = "arn";

        public const string XRaySDK = "sdk";
        public const string XRayVersion = "sdk_version";
        public const string XRayAutoInstr = "auto_instrumentation";







    }
}
