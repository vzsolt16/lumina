output "aws_region" {
  description = "AWS region selected for this environment."
  value       = var.aws_region
}

output "name_prefix" {
  description = "Naming prefix for resources created by future modules."
  value       = local.name_prefix
}

output "common_tags" {
  description = "Common tags supplied to future infrastructure modules."
  value       = local.common_tags
}

