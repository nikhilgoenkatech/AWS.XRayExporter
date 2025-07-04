# Xray Connector MSSQL Kubernetes Deployment

This script automates the deployment of X-RAY collector as mentioned [here](https://raw.githubusercontent.com/nikhilgoenkatech/AWS.XRayExporter/refs/heads/master/readme.md)

---

## 🔧 Features

- Optional **dry-run** mode to preview changes without applying them.
- Interactive prompts for secrets and configuration values.
- MSSQL StatefulSet deployment.
- Helm-based installation of KEDA.
- Custom Docker image support.
- Environment-based templating using `envsubst`.
- Generation of a reusable `.env` file for future use.

---

## 🛠️ Prerequisites

Ensure the following tools are installed and configured on your system before running the setup script:

### 1. Set up AWS CLI to point to your EKS cluster

```bash
aws eks update-kubeconfig --region us-east-2 --name cluster-name
```

Replace `us-east-2` and `cluster-name` with your appropriate region and cluster name if different.

### 2. Install `envsubst` (part of GNU `gettext`)

```bash
sudo yum install -y gettext
```


### 3. Install Helm (v3)

```bash
curl -fsSL -o get_helm.sh https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3
chmod 700 get_helm.sh
./get_helm.sh
helm version
```

### 4. Install ekstcl  
```bash
curl --silent --location "https://github.com/weaveworks/eksctl/releases/latest/download/eksctl_$(uname -s)_amd64.tar.gz" -o eksctl.tar.gz

# Extract the tarball
tar -xzf eksctl.tar.gz

# Move the binary to a directory in your PATH
sudo mv eksctl /usr/local/bin

# Verify installation
eksctl version
```

### 5. Other pre-requisites  
- Access to a Kubernetes cluster with storage class support.
- Optional: Docker and access to a container registry (if you build your own image).
- Valid AWS IAM role or access/secret keys (if integrating with AWS).
- OpenSSL for token generation.  
---  

## ➕ Add-ons: EBS CSI Driver  
To enable dynamic volume provisioning (e.g., for MSSQL), install the AWS EBS CSI driver.  
Install the EBS CSI Add-on

```bash
eksctl create addon \
  --name aws-ebs-csi-driver \
  --cluster CLUSTER_NAME \
  --region REGION \
  --service-account-role-arn arn:aws:iam::AWS_ACCOUNT_ID:role/AmazonEKS_EBS_CSI_DriverRole \
  --force
```
🔁 Replace CLUSTER_NAME, REGION, and AWS_ACCOUNT_ID with your actual values.


# Script & Execution  
## 🧪 Dry Run Mode

You can test the script without applying changes:

```bash
./deploy.sh --dry-run
```

This will simulate and preview the commands and Kubernetes manifests.

---

## 📦 How to Use

### Step 1: Optional - Build and Push Your Own Docker Image

Uncomment these lines in the script if you wish to use your own image:
```bash
# docker build -t xrayconnector:latest -f ./xrayconnector/Dockerfile .
# docker tag xrayconnector:latest <your-docker-repo>/xrayconnector:latest
# docker push <your-docker-repo>/xrayconnector:latest
```

Also update the `xrayconnector.yml` file with the new image name.

---

### Step 2: Install KEDA

KEDA (Kubernetes Event-Driven Autoscaling) will be installed or upgraded automatically.

---

### Step 3: Input Configuration

The script prompts you for:
- `MSSQL_SA_PASSWORD`
- `AWS_RoleArn` or access keys
- `OTLP_ENDPOINT` and token
- Polling interval
- Auto-start flag

---

### Step 4: Deploy Resources

The script:
- Applies storage class
- Creates MSSQL StatefulSet and headless service
- Initializes database
- Applies secrets and config maps
- Deploys the Xray connector

---

## Output

A file named `xray-config.env` will be created with all the key environment variables used. You can reuse this file to reapply configuration or troubleshoot.

---

## 🛑 Notes

- The MSSQL password must be complex and will be masked during input.
- If a pod rollout or SQL command fails, the script will stop and show the error.
- The MSSQL StatefulSet assumes storageclass is already defined via `storageclass.yaml`.

---

## Files in This Project

- `deploy-xrayconnector.sh` — The main deployment script.
- `mssql-statefulset.yml`, `mssql-headless.yml`, `mssql-statefulset-secrets.yml` — Kubernetes manifests.
- `storageclass.yaml` — Storage class configuration.
- `connector-config.yml`, `xrayconnector.yml` — Connector deployment files.
- `xray-config.env` — Generated file storing config.

---

## Environment variables from xray-config.env   

| Variable | Description |
|----------|-------------|
| `MSSQL_SA_PASSWORD` | Password for the MSSQL `sa` user |
| `SQLDB_Connection` | Connection string for the MSSQL database |
| `AWS_RoleArn` | IAM Role ARN for role-based access (optional) |
| `AWS_IdentityKey` | AWS access key ID (if not using role-based access) |
| `AWS_SecretKey` | AWS secret access key (if not using role-based access) |
| `OTLP_ENDPOINT` | OTLP endpoint for trace ingestion |
| `OTLP_HEADER_AUTHORIZATION` | Authorization header for OTLP (formatted as `Api-Token <token>`) |
| `PollingIntervalSeconds` | Interval in seconds for polling (default: 300) |
| `AutoStart` | Whether to auto-start the connector (default: True) |
| `WATCHDOG_BASE_KEY` | Randomly generated key for internal use | 

## Troubleshooting tips  
If you encounter errors like below:  
```
ProvisioningFailed: failed to provision volume with StorageClass "gp2-immediate": ...
InvalidIdentityToken: No OpenIDConnect provider found in your account for https://oidc.eks.REGION.amazonaws.com/id/...
```

It likely means the OIDC provider is not associated with the cluster. To address it, follow these steps:
1. Associate the OIDC Provider  
```bash
eksctl utils associate-iam-oidc-provider \
  --cluster CLUSTER_NAME \
  --region REGION \
  --approve
```

2. Create IAM Role for the CSI Driver
```bash
eksctl create iamserviceaccount \
  --name ebs-csi-controller-sa \
  --namespace kube-system \
  --cluster CLUSTER_NAME \
  --region REGION \
  --attach-policy-arn arn:aws:iam::aws:policy/service-role/AmazonEBSCSIDriverPolicy \
  --approve \
  --role-name AmazonEKS_EBS_CSI_DriverRole
```



