# 🚀 EKS Cluster Automation Script

This `create-cluster.sh` script automates the provisioning of an Amazon EKS cluster along with its supporting VPC infrastructure, subnets, routing, and a managed node group.

---

## ✅ Features

- Creates a custom VPC with DNS support
- Provisions public and private subnets across 3 Availability Zones
- Sets up Internet Gateway and NAT Gateway
- Configures route tables for public and private subnets
- Creates an EKS cluster with private endpoint access
- Provisions a managed node group with SSH access
- Tags resources for Kubernetes discovery

---

## 🧰 Prerequisites

- **AWS CLI v2** installed and configured (`aws configure`)
- **IAM roles** with the following policies:
  - `AmazonEKSClusterPolicy` (for `ROLE_NAME`)
  - `AmazonEKSWorkerNodePolicy`, `AmazonEC2ContainerRegistryReadOnly`, `AmazonEKS_CNI_Policy` (for `NODE_ROLE_NAME`)
- **An EC2 Key Pair** in the target region (for SSH access)
- **Sufficient AWS quotas** for VPCs, subnets, NAT gateways, etc.

---

## ⚙️ Configuration

Update the following variables at the top of the script:

```bash
REGION="ap-southeast-2"             # AWS region
CLUSTER_NAME="myekscluster"         # EKS cluster name
VPC_CIDR="10.100.0.0/16"            # CIDR block for the VPC
KEY_NAME="your-key-name"            # EC2 key pair name
AWS_ACCOUNT_ID="123456789012"       # Your AWS account ID
ROLE_NAME="EKSClusterRole"          # IAM role for EKS cluster
NODE_ROLE_NAME="EKSNodeRole"        # IAM role for worker nodes
EBS_ROLE_NAME="EBSCSIRole"          # (Optional) Role for EBS CSI driver
```

---

## ▶️ Usage

Make the script executable and run it:

```bash
chmod +x create-cluster.sh
./create-cluster.sh
```

The script will:

1. Create a VPC with public and private subnets  
2. Set up Internet Gateway and NAT Gateway  
3. Configure route tables for public and private subnets  
4. Create an EKS cluster with private subnets
5. Wait for the cluster to become active
6. Create a managed node group with SSH access

---

