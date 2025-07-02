# EKS Cluster Automation Script

This `create-cluster.sh` bash script automates the provisioning of an Amazon Elastic Kubernetes Service (EKS) cluster along with the necessary networking infrastructure and add-ons.

---

## Features

- Creates a custom VPC with public and private subnets across 3 Availability Zones
- Configures Internet Gateway and NAT Gateway
- Sets up route tables for public and private subnets
- Provisions an EKS cluster using AWS CLI
- Creates a managed node group with SSH access
- Installs the AWS EBS CSI driver add-on

---

## Prerequisites

- AWS CLI v2 installed and configured (`aws configure`)
- IAM roles with the following policies:
  - `AmazonEKSClusterPolicy`
  - `AmazonEKSWorkerNodePolicy`
  - `AmazonEC2ContainerRegistryReadOnly`
  - `AmazonEKS_CNI_Policy`
- An existing EC2 key pair in the target region
- Sufficient AWS service quotas (VPCs, subnets, NAT gateways, etc.)

---

## Configuration

Update the following variables at the top of the script:

```bash
REGION="us-east-2"
CLUSTER_NAME="myekscluster"
VPC_CIDR="10.100.0.0/16"
KEY_NAME="MYKEY"  # Replace with your EC2 key pair name
AWS_ACCOUNT_ID="your-account-id"
ROLE_NAME="your-iam-role-name"  
```

## Usage  
Make the script executable and run it:  

```bash
chmod +x create-cluster.sh
./create-cluster.sh
```

The script will:

Create a VPC with public and private subnets
Set up routing and NAT gateway
Create an EKS cluster and wait for it to become active
Create a managed node group
Install the EBS CSI driver add-on  

## 🧹 Cleanup  
To delete the cluster and associated resources:

```bash
aws eks delete-cluster --name myekscluster --region us-east-2
```
