locals {
  common_tags = merge(
    {
      Project     = var.project_name
      ManagedBy   = "Terraform"
      Environment = "bootstrap"
      NameSuffix  = var.resource_suffix
    },
    var.common_tags
  )
}

