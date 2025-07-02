#!/bin/bash -x
set -e

# Configuration
REGION="us-east-2"
CLUSTER_NAME="myekscluster" #rename as required
VPC_CIDR="10.100.0.0/16"
TAG="eks-vpc"
KEY_NAME="MYKEY"  # replace it with any existing keys in the region 
AWS_ACCOUNT_ID="account-id" #replace with account-id
ROLE_NAME="role-name" #Replace with role policies - AmazonEKSWorkerNodePolicy, AmazonEC2ContainerRegistryReadOnly, AmazonEKS_CNI_Policy, AmazonEKSClusterPolicy 


# Create VPC
VPC_ID=$(aws ec2 create-vpc --cidr-block $VPC_CIDR --region $REGION --query "Vpc.VpcId" --output text)
aws ec2 modify-vpc-attribute --vpc-id $VPC_ID --enable-dns-support '{"Value":true}' --region $REGION
aws ec2 modify-vpc-attribute --vpc-id $VPC_ID --enable-dns-hostnames '{"Value":true}' --region $REGION

# Create Internet Gateway
IGW_ID=$(aws ec2 create-internet-gateway --region $REGION --query "InternetGateway.InternetGatewayId" --output text)
aws ec2 attach-internet-gateway --vpc-id $VPC_ID --internet-gateway-id $IGW_ID --region $REGION

# Create Public Subnets
AZS=($(aws ec2 describe-availability-zones --region $REGION --query "AvailabilityZones[*].ZoneName" --output text))
PUB_SUBNETS=()
for i in {0..2}; do
  CIDR="10.100.$((i*2)).0/24"
  SUBNET_ID=$(aws ec2 create-subnet --vpc-id $VPC_ID --cidr-block $CIDR --availability-zone ${AZS[$i]} --region $REGION --query "Subnet.SubnetId" --output text)
  aws ec2 create-tags --resources $SUBNET_ID --tags Key=Name,Value=eks-public-$i Key=kubernetes.io/role/elb,Value=1 Key=kubernetes.io/cluster/$CLUSTER_NAME,Value=shared --region $REGION
  PUB_SUBNETS+=($SUBNET_ID)
done

# Create Private Subnets
PRIV_SUBNETS=()
for i in {0..2}; do
  CIDR="10.100.$((i*2+1)).0/24"
  SUBNET_ID=$(aws ec2 create-subnet --vpc-id $VPC_ID --cidr-block $CIDR --availability-zone ${AZS[$i]} --region $REGION --query "Subnet.SubnetId" --output text)
  aws ec2 create-tags --resources $SUBNET_ID --tags Key=Name,Value=eks-private-$i Key=kubernetes.io/role/internal-elb,Value=1 Key=kubernetes.io/cluster/$CLUSTER_NAME,Value=shared --region $REGION
  PRIV_SUBNETS+=($SUBNET_ID)
done

# Create Route Table for Public Subnets
PUB_RT_ID=$(aws ec2 create-route-table --vpc-id $VPC_ID --region $REGION --query "RouteTable.RouteTableId" --output text)
aws ec2 create-route --route-table-id $PUB_RT_ID --destination-cidr-block 0.0.0.0/0 --gateway-id $IGW_ID --region $REGION
for SUBNET_ID in "${PUB_SUBNETS[@]}"; do
  aws ec2 associate-route-table --subnet-id $SUBNET_ID --route-table-id $PUB_RT_ID --region $REGION
done

# Allocate available Elastic IP
EIP_ALLOC_ID=$(aws ec2 describe-addresses --query "Addresses[?AssociationId==null].[PublicIp,AllocationId]" --output text | awk '{print $2}')

# Create a single NAT Gateway in the first public subnet
NAT_GW_ID=$(aws ec2 create-nat-gateway \
  --subnet-id ${PUB_SUBNETS[0]} \
  --allocation-id $EIP_ALLOC_ID \
  --region $REGION \
  --query "NatGateway.NatGatewayId" \
  --output text)

# Wait for NAT Gateway to become available
echo "Waiting for NAT Gateway to become available..."
aws ec2 wait nat-gateway-available --nat-gateway-ids $NAT_GW_ID --region $REGION

# Create route tables for private subnets and associate them with the single NAT Gateway
for i in {0..2}; do
  RT_ID=$(aws ec2 create-route-table --vpc-id $VPC_ID --region $REGION --query "RouteTable.RouteTableId" --output text)
  aws ec2 create-route --route-table-id $RT_ID --destination-cidr-block 0.0.0.0/0 --nat-gateway-id $NAT_GW_ID --region $REGION
  aws ec2 associate-route-table --subnet-id ${PRIV_SUBNETS[$i]} --route-table-id $RT_ID --region $REGION
done

# Create EKS Cluster
aws eks create-cluster   --name $CLUSTER_NAME   --region $REGION   --role-arn arn:aws:iam::$AWS_ACCOUNT_ID:role/role-name   --resources-vpc-config subnetIds=$(IFS=,; echo "${PRIV_SUBNETS[*]}"),endpointPublicAccess=true   --kubernetes-version 1.32

echo "Waiting for EKS cluster to become ACTIVE..."
aws eks wait cluster-active --name $CLUSTER_NAME --region $REGION

# Create Node Group
aws eks create-nodegroup \
  --cluster-name "$CLUSTER_NAME" \
  --region "$REGION" \
  --nodegroup-name linux-nodes \
  --node-role arn:aws:iam::$AWS_ACCOUNT_ID:role/$ROLE_NAME \
  --subnets "$(printf '["%s"]' "$(IFS=','; echo "${PRIV_SUBNETS[*]}")" | sed 's/,/","/g')" \
  --scaling-config minSize=1,maxSize=3,desiredSize=2 \
  --disk-size 20 \
  --instance-types t3.medium \
  --ami-type AL2_x86_64 \
  --remote-access ec2SshKey="$KEY_NAME" \
  --tags Name=eks-nodegroup

# Add an add-on for ebs csi driver
aws eks create-addon \
  --cluster-name $CLUSTER_NAME \
  --region $REGION \
  --addon-name aws-ebs-csi-driver \
  --service-account-role-arn arn:aws:iam::$AWS_ACCOUNT_ID:role/$ROLE_NAME \
  --resolve-conflicts OVERWRITE
