# Lumina Terraform foundation

This directory contains an isolated bootstrap stack and environment configurations. It does not create application infrastructure yet.

## Structure

- `bootstrap/`: creates the dedicated S3 bucket used for Terraform state. Its own state remains local.
- `environments/dev/`: dev root configuration, prepared to use S3 state with native S3 lockfiles.
- `modules/`: reserved for future reusable networking, IAM, ECR, EKS, and storage modules.

Terraform requires version 1.10 or later for native S3 lockfiles. The AWS provider is constrained to major version 6. Resources use the `project-environment-suffix` naming prefix where appropriate and receive common tags through provider default tags.

## AWS CLI setup

Configure credentials using your organization’s approved method (for example, IAM Identity Center):

```sh
aws configure sso
aws sso login --profile lumina
export AWS_PROFILE=lumina
aws configure set region eu-north-1 --profile lumina
aws sts get-caller-identity
```

Do not put credentials in Terraform files or tfvars. Terraform uses the AWS CLI credential chain. The configured identity needs permission to create the state bucket during bootstrap and access it afterward, plus permissions for any future infrastructure.

## Bootstrap remote state

The state bucket must exist before initializing the dev backend. The bootstrap configuration uses local state and creates only an S3 bucket with versioning, server-side encryption, and all public access blocked. It does not create a DynamoDB table.

The default bucket name is `lumina-terraform-state-zsolt`; S3 names are globally unique, so change it in `bootstrap/terraform.tfvars` if it is already taken. Run:

```sh
cd infrastructure/terraform/bootstrap
terraform init
terraform fmt -check -recursive
terraform validate
terraform plan
terraform apply
terraform output -raw state_bucket_name
```

Review the plan before applying. This is the one-time state-bucket setup; bootstrap state remains in this directory's local `terraform.tfstate` and must be kept securely backed up. Do not commit it.

## Initialize dev

Copy the backend example and set its bucket name to the value returned by bootstrap:

```sh
cd ../environments/dev
cp backend.hcl.example backend.hcl
# Edit backend.hcl if you chose a different bucket name
terraform init -backend-config=backend.hcl
terraform fmt -check -recursive
terraform validate
```

The backend key is `environments/dev/terraform.tfstate`; `use_lockfile = true` enables S3-native state locking. The backend configuration file is local and ignored by git. Terraform will create the state object when infrastructure is first applied. No apply is required to initialize or validate this empty dev foundation.

## Format and validate

From `infrastructure/terraform`, run:

```sh
terraform fmt -check -recursive
terraform -chdir=bootstrap init
terraform -chdir=bootstrap validate
terraform -chdir=environments/dev init -backend=false
terraform -chdir=environments/dev validate
```

When network state access and the bucket are available, initialize dev with its backend file as shown above. Neither validation nor initialization provisions AWS resources.

