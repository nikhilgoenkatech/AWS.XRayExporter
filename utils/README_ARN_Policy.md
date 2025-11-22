# IRSA Configuration for EKS and X-Ray Integration

This guide outlines the steps to configure and test an IAM Role for Service Accounts (IRSA) setup in Amazon EKS for X-Ray integration. Sensitive information such as account numbers, role names, and cluster-specific details have been replaced with generic placeholders.

---

## Steps to Configure and Verify IRSA

### 1. Verify Role-Based Settings
Run the following commands to verify the IAM role and OIDC configuration:

1. **Retrieve OIDC Issuer URL for the Cluster**:
   ```bash
   aws eks describe-cluster --name <CLUSTER_NAME> --query "cluster.identity.oidc.issuer"
   ```

2. **View the Role's Trust Policy**:
   ```bash
   aws iam get-role --role-name <ROLE_NAME> --query 'Role.AssumeRolePolicyDocument' --output json
   ```

3. **Update the Role's Trust Policy**:
   ```bash
   aws iam update-assume-role-policy --role-name <ROLE_NAME> --policy-document file://trust-policy.json
   ```

4. **Annotate the Service Account with the IAM Role**:
   ```bash
   kubectl annotate serviceaccount <xrayconnector-function-keys-identity-svc-act> eks.amazonaws.com/role-arn=arn:aws:iam::<ACCOUNT_ID>:role/<ROLE_NAME> -n <NAMESPACE>
   ```

---

### 2. Test IRSA Configuration Inside a Container
To verify that the IRSA setup is working, perform the following steps inside a container:

1. **Connect to the Pod**:
   ```bash
   kubectl exec -it <POD_NAME> -n <NAMESPACE> -- bash
   ```

2. **Install AWS CLI**:
   ```bash
   apt-get update && apt-get install -y curl unzip
   curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
   unzip awscliv2.zip
   ./aws/install
   ```

3. **Verify Caller Identity**:
   ```bash
   aws --region <REGION> sts get-caller-identity
   ```

4. **Test Role Assumption**:
   ```bash
   aws sts assume-role-with-web-identity        --role-arn arn:aws:iam::<ACCOUNT_ID>:role/<ROLE_NAME>        --role-session-name <SESSION_NAME>        --web-identity-token file:///var/run/secrets/eks.amazonaws.com/serviceaccount/token        --duration-seconds 3600        --region <REGION>
   ```

5. **Fetch X-Ray Trace Summaries**:
   ```bash
   aws xray get-trace-summaries        --start-time $(date -u -d '-5 minutes' +%s)        --end-time $(date -u +%s)        --region <REGION>
   ```

---

### 3. Sample Trust Policies

#### Strict Trust Policy (Namespace-Specific)
Use this policy to restrict role assumption to specific namespaces.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/<OIDC_PROVIDER>"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "<OIDC_PROVIDER>:sub": [
            "system:serviceaccount:<NAMESPACE_1>:<xrayconnector-function-keys-identity-svc-act>",
            "system:serviceaccount:<NAMESPACE_2>:<xrayconnector-function-keys-identity-svc-act>"
          ]
        }
      }
    }
  ]
}
```

#### Flexible Trust Policy (All Namespaces)
Use this policy for a more flexible configuration that supports all namespaces.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/<OIDC_PROVIDER>"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "<OIDC_PROVIDER>:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "<OIDC_PROVIDER>:sub": "system:serviceaccount:*:<xrayconnector-function-keys-identity-svc-act>"
        }
      }
    }
  ]
}
```

---

## Notes
- Replace the placeholders (`<ACCOUNT_ID>`, `<ROLE_NAME>`, `<CLUSTER_NAME>`, `<NAMESPACE>`, `<REGION>`, `<OIDC_PROVIDER>`, etc.) with the appropriate values for your environment.
- Ensure that the trust policy aligns with your security requirements and restricts access to only the necessary namespaces and service accounts.

---

## Troubleshooting
- If the `aws sts get-caller-identity` command does not return the expected role, double-check the trust policy and service account annotations.
- Verify that the OIDC provider is correctly configured for your EKS cluster.

---

## References
- [Amazon EKS IRSA Documentation](https://docs.aws.amazon.com/eks/latest/userguide/iam-roles-for-service-accounts.html)
- [AWS X-Ray Documentation](https://docs.aws.amazon.com/xray/latest/devguide/aws-xray.html)
