[CmdletBinding()]
param(
    [string]$Region = "us-east-1",
    [string]$ProjectName = "tec-fuelmix",
    [string]$Tag = "",
    [switch]$PlanOnly,
    [switch]$SkipSmoke
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$EvidenceDir = Join-Path $RepoRoot "docs\evidence"
$TerraformDir = Join-Path $RepoRoot "infra\terraform"

Set-Location -LiteralPath $RepoRoot

$SmokeBearerToken = [System.Environment]::GetEnvironmentVariable("READ_API_BEARER_TOKEN")

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = (git rev-parse --short HEAD).Trim()
}

if (-not $SkipSmoke -and [string]::IsNullOrWhiteSpace($SmokeBearerToken)) {
    throw "Set READ_API_BEARER_TOKEN in the environment unless -SkipSmoke is used."
}

New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null

function Assert-LambdaInvokeSucceeded {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TranscriptPath,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $invoke = Get-Content -LiteralPath $TranscriptPath -Raw | ConvertFrom-Json
    if ($invoke.FunctionError) {
        throw "$Name lambda returned FunctionError=$($invoke.FunctionError)."
    }
}

$AccountId = (aws sts get-caller-identity --query Account --output text).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "aws sts get-caller-identity failed."
}

$Registry = "$AccountId.dkr.ecr.$Region.amazonaws.com"
$Images = @{
    Fetch = "$Registry/$ProjectName-fetch:$Tag"
    Writer = "$Registry/$ProjectName-writer:$Tag"
    ReadApi = "$Registry/$ProjectName-read-api:$Tag"
    Migrator = "$Registry/$ProjectName-migrator:$Tag"
}

$env:TF_VAR_aws_region = $Region
$env:TF_VAR_fetch_lambda_image_uri = $Images.Fetch
$env:TF_VAR_writer_lambda_image_uri = $Images.Writer
$env:TF_VAR_read_api_lambda_image_uri = $Images.ReadApi
$env:TF_VAR_migrator_lambda_image_uri = $Images.Migrator

terraform -chdir=infra/terraform init
if ($LASTEXITCODE -ne 0) {
    throw "terraform init failed."
}

terraform -chdir=infra/terraform apply -auto-approve `
    -target=aws_ecr_repository.fetch `
    -target=aws_ecr_repository.writer `
    -target=aws_ecr_repository.read_api `
    -target=aws_ecr_repository.migrator
if ($LASTEXITCODE -ne 0) {
    throw "terraform ECR bootstrap failed."
}

aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $Registry
if ($LASTEXITCODE -ne 0) {
    throw "docker login to ECR failed."
}

docker build -f .\src\TecFuelMix.FetchLambda\Dockerfile -t $Images.Fetch .
if ($LASTEXITCODE -ne 0) { throw "fetch image build failed." }
docker build -f .\src\TecFuelMix.WriterLambda\Dockerfile -t $Images.Writer .
if ($LASTEXITCODE -ne 0) { throw "writer image build failed." }
docker build -f .\src\TecFuelMix.ReadApiLambda\Dockerfile -t $Images.ReadApi .
if ($LASTEXITCODE -ne 0) { throw "read API image build failed." }
docker build -f .\src\TecFuelMix.MigratorLambda\Dockerfile -t $Images.Migrator .
if ($LASTEXITCODE -ne 0) { throw "migrator image build failed." }

docker push $Images.Fetch
if ($LASTEXITCODE -ne 0) { throw "fetch image push failed." }
docker push $Images.Writer
if ($LASTEXITCODE -ne 0) { throw "writer image push failed." }
docker push $Images.ReadApi
if ($LASTEXITCODE -ne 0) { throw "read API image push failed." }
docker push $Images.Migrator
if ($LASTEXITCODE -ne 0) { throw "migrator image push failed." }

terraform -chdir=infra/terraform fmt -check
if ($LASTEXITCODE -ne 0) { throw "terraform fmt -check failed." }
terraform -chdir=infra/terraform validate
if ($LASTEXITCODE -ne 0) { throw "terraform validate failed." }

terraform -chdir=infra/terraform plan -out=tfplan *>&1 |
    Tee-Object -FilePath (Join-Path $EvidenceDir "aws-terraform-plan.txt")
if ($LASTEXITCODE -ne 0) { throw "terraform plan failed." }

if ($PlanOnly) {
    Write-Host "Plan written to docs\evidence\aws-terraform-plan.txt. Skipping apply because -PlanOnly was set."
    exit 0
}

terraform -chdir=infra/terraform apply tfplan *>&1 |
    Tee-Object -FilePath (Join-Path $EvidenceDir "aws-terraform-apply.txt")
if ($LASTEXITCODE -ne 0) { throw "terraform apply failed." }

$MigratorFunction = (terraform -chdir=infra/terraform output -raw migrator_lambda_name).Trim()
$MigrationResponse = Join-Path $EvidenceDir "aws-migration-response.json"
aws lambda invoke --region $Region --function-name $MigratorFunction $MigrationResponse *>&1 |
    Tee-Object -FilePath (Join-Path $EvidenceDir "aws-migration.txt")
if ($LASTEXITCODE -ne 0) { throw "migrator lambda invoke failed." }

Assert-LambdaInvokeSucceeded -TranscriptPath (Join-Path $EvidenceDir "aws-migration.txt") -Name "migrator"

if (-not $SkipSmoke) {
    $FetchFunction = "$ProjectName-fetch"
    $FetchInvokeTranscript = Join-Path $EvidenceDir "aws-fetch-invoke.txt"
    aws lambda invoke --region $Region --function-name $FetchFunction (Join-Path $EvidenceDir "aws-fetch-invoke-response.json") *>&1 |
        Tee-Object -FilePath $FetchInvokeTranscript
    if ($LASTEXITCODE -ne 0) { throw "fetch lambda invoke failed." }
    Assert-LambdaInvokeSucceeded -TranscriptPath $FetchInvokeTranscript -Name "fetch"

    Start-Sleep -Seconds 30

    $RawQueueUrl = (terraform -chdir=infra/terraform output -raw raw_snapshot_queue_url).Trim()
    aws sqs get-queue-attributes --region $Region --queue-url $RawQueueUrl --attribute-names ApproximateNumberOfMessages ApproximateNumberOfMessagesNotVisible ApproximateAgeOfOldestMessage |
        Set-Content -LiteralPath (Join-Path $EvidenceDir "aws-queue-after-fetch.json")

    $ReadUrl = (terraform -chdir=infra/terraform output -raw read_api_invoke_url).Trim()
    $ApiKey = (terraform -chdir=infra/terraform output -raw read_api_key_value).Trim()
    $Headers = @{
        "x-api-key" = $ApiKey
        "Authorization" = "Bearer $SmokeBearerToken"
    }

    Invoke-RestMethod -Uri $ReadUrl -Headers $Headers |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath (Join-Path $EvidenceDir "aws-read-api-smoke.txt")
}

Write-Host "Deployment complete for tag $Tag."
