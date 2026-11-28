variable "aws_region" {
  description = "AWS region for dev infrastructure."
  type        = string
  default     = "eu-north-1"
}

variable "project_name" {
  description = "Project name used in resource names and tags."
  type        = string
  default     = "lumina"
}

variable "environment" {
  description = "Deployment environment name."
  type        = string
  default     = "dev"
}

variable "resource_suffix" {
  description = "Suffix added to project resource names where appropriate."
  type        = string
  default     = "zsolt"
}

variable "common_tags" {
  description = "Additional tags applied to resources in this environment."
  type        = map(string)
  default     = {}
}

