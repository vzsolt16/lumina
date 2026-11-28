locals {
  name_prefix = "${var.project_name}-${var.environment}-${var.resource_suffix}"

  common_tags = merge(
    {
      Project     = var.project_name
      Environment = var.environment
      ManagedBy   = "Terraform"
      NameSuffix  = var.resource_suffix
    },
    var.common_tags
  )
}

