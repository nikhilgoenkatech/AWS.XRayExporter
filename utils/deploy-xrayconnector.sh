#!/bin/bash 

set -euo pipefail


# Setting BASE_DIR to the root of the project (one level above this script) as config files are accessible at that location
BASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRY_RUN=false

if [[ "${1:-}" == "--dry-run" ]]; then
  DRY_RUN=true
  echo "Running in dry-run mode: no changes will be applied to the cluster."
fi

run_cmd() {
  echo "Running: $*"
  if $DRY_RUN; then
    echo "Simulating: $*"
  else
    if ! output=$("$@" 2>&1); then
      echo "Command failed: $*"
      echo "Output:"
      echo "$output"
      exit 1
    fi
  fi
}

prompt() {
  local var_name=$1
  local prompt_text=$2
  local default_value=${3:-}
  local silent=${4:-false}

  if [ "$silent" = true ]; then
    read -sp "$prompt_text" input_value
    echo
  else
    read -p "$prompt_text" input_value
  fi

  if [ -z "$input_value" ] && [ -n "$default_value" ]; then
    input_value="$default_value"
  fi

  export "$var_name"="$input_value"
}

echo "Step 1: The default Docker image nikhilgoenka/xrayconnector is currently in use.
If you prefer to build and push your own image, uncomment the steps below and update xrayconnector.yml to reference your custom image. "
#run_cmd docker build -t xrayconnector:latest -f ./xrayconnector/Dockerfile .
#prompt "DOCKER_REPO" "Enter your Docker repository (e.g., docker.io/username): "
#run_cmd docker tag xrayconnector:latest "$DOCKER_REPO/xrayconnector:latest"
#run_cmd docker push "$DOCKER_REPO/xrayconnector:latest"

echo "Step 2: Install KEDA"
run_cmd helm repo add kedacore https://kedacore.github.io/charts
run_cmd helm repo update
run_cmd bash -c 'helm install keda kedacore/keda --namespace keda --create-namespace || helm upgrade keda kedacore/keda --namespace keda'

echo ""
echo "Step 3: Collect Configuration Inputs"
echo "----------------------------------------"
# MSSQL Configuration
prompt "MSSQL_SA_PASSWORD" "Enter MSSQL_SA_PASSWORD (Password should be complex with atleast 1 capital letter, number and symbol): " "" true

echo ""
echo "AWS Credentials"
prompt "AWS_RegionEndpoint" "Enter AWS_RegionEndpoint (us-east-1, us-east-2..): "
prompt "AWS_RoleArn" "Enter AWS_RoleArn (if using role-based access, else leave blank): "
prompt "AWS_IdentityKey" "Enter AWS_IdentityKey (if not using role-based access, else leave blank): "
prompt "AWS_SecretKey" "Enter AWS_SecretKey (if not using role-based access, else leave blank): " "" true

echo ""
echo "Dynatrace OTLP Configuration"
prompt "OTLP_ENDPOINT" "Enter OTLP_ENDPOINT (eg:https://xxx.live.dynatrace.com/api/v2/otlp/v1/traces) "
prompt "INGEST_TOKEN" "Enter OTLP_TRACE_INGEST_TOKEN: " "" true

echo ""
echo "XRayCollector Job Configuration"
prompt "PollingIntervalSeconds" "Enter PollingIntervalSeconds (default 300): " "300"
prompt "AutoStart" "Enable AutoStart? (True/False, default True): " "True"

SQLDB_Connection="Server=mssqlinst.mssql.svc.cluster.local;Database=DurableDB;User ID=sa;Password=${MSSQL_SA_PASSWORD};Persist Security Info=False;TrustServerCertificate=True;Encrypt=True;"
export SQLDB_Connection

# AWS Configuration
export AWS_RegionEndpoint
if [[ -n "$AWS_RoleArn" ]]; then
  export AWS_RoleArn
else
  export AWS_IdentityKey
  export AWS_SecretKey
fi

#OTLP configuration
export OTLP_ENDPOINT
export OTLP_HEADER_AUTHORIZATION="Api-Token ${INGEST_TOKEN}"
export PollingIntervalSeconds
export AutoStart
export WATCHDOG_BASE_KEY="$(openssl rand -base64 32 | base64)"

echo "Step 4: Apply Kubernetes manifests"

if $DRY_RUN; then
  echo "Previewing mssql namespace manifest:"
  kubectl create namespace mssql --dry-run=client -o yaml | tee rendered-mssql-config.yaml
else
  kubectl create namespace mssql --dry-run=client -o yaml | run_cmd kubectl apply -f -
fi

run_cmd kubectl apply -f "$BASE_DIR/storageclass.yaml"

if $DRY_RUN; then
  echo "Previewing mssql-statefulset-secrets.yml:"
  envsubst < "$BASE_DIR/mssql-statefulset-secrets.yml" | tee rendered-mssql-statefulset-secrets.yml
else
  envsubst < "$BASE_DIR/mssql-statefulset-secrets.yml" | run_cmd kubectl -n mssql apply -f -
fi

#Not applicable for dry-run
if ! $DRY_RUN; then
  run_cmd kubectl apply -f "$BASE_DIR/mssql-statefulset.yml" -n mssql
  run_cmd kubectl rollout status statefulset mssql -n mssql
  run_cmd kubectl apply -f "$BASE_DIR/mssql-headless.yml"
fi

if ! $DRY_RUN; then
  export mssqlPod=$(kubectl get pods -n mssql -o jsonpath='{.items[0].metadata.name}')
  sleep 10
  run_cmd kubectl exec -n mssql "$mssqlPod" -- /opt/mssql-tools18/bin/sqlcmd -C -S . -U sa -P "$MSSQL_SA_PASSWORD" -Q "CREATE DATABASE [DurableDB] COLLATE Latin1_General_100_BIN2_UTF8"
fi

if $DRY_RUN; then
  echo "Previewing connector-config.yml:"
  envsubst < "$BASE_DIR/connector-config.yml" | tee rendered-connector-config.yml
else
  envsubst < "$BASE_DIR/connector-config.yml" | run_cmd kubectl apply -f -
fi

if $DRY_RUN; then
  echo "Previewing xrayconnector.yml:"
  envsubst < "$BASE_DIR/xrayconnector.yml" | tee rendered-xrayconnector.yml
else
  envsubst < "$BASE_DIR/xrayconnector.yml" | run_cmd kubectl apply -f -
  run_cmd kubectl rollout restart deployment xrayconnector
  run_cmd kubectl wait --for=condition=Ready pods --all -n mssql --timeout=180s
fi

cat <<EOF > xray-config.env
MSSQL_SA_PASSWORD=$MSSQL_SA_PASSWORD
SQLDB_Connection=$SQLDB_Connection
AWS_RegionEndpoint=$AWS_RegionEndpoint
AWS_RoleArn=$AWS_RoleArn
AWS_IdentityKey=$AWS_IdentityKey
AWS_SecretKey=$AWS_SecretKey
OTLP_ENDPOINT=$OTLP_ENDPOINT
OTLP_HEADER_AUTHORIZATION=$OTLP_HEADER_AUTHORIZATION
PollingIntervalSeconds=$PollingIntervalSeconds
AutoStart=$AutoStart
EOF

echo "Configuration saved to xray-config.env"
