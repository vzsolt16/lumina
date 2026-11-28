variable "aws_region" {
  description = "AWS region for the Terraform state bucket."
  type        = string
  default     = "eu-north-1"
}

variable "project_name" {
  description = "Project name used in resource tags."
  type        = string
  default     = "lumina"
}

variable "resource_suffix" {
  description = "Project naming suffix."
  type        = string
  default     = "zsolt"
}

variable "state_bucket_name" {
  description = "Globally unique S3 bucket name for Terraform state."
  type        = string
  default     = "lumina-terraform-state-zsolt"
}

variable "common_tags" {
  description = "Additional tags applied to bootstrap resources."
  type        = map(string)
  default     = {}
}

