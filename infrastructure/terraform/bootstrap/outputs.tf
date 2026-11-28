output "state_bucket_name" {
  description = "S3 bucket name to use in the dev backend configuration."
  value       = aws_s3_bucket.terraform_state.bucket
}

output "state_bucket_arn" {
  description = "ARN of the Terraform state bucket."
  value       = aws_s3_bucket.terraform_state.arn
}

