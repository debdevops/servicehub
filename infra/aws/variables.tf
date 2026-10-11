variable "region" {
  description = "AWS region, for example eu-west-1. Pick the one your queues are in."
  type        = string
}

variable "name" {
  description = "Prefix for every resource name. Lower-case letters, digits and hyphens."
  type        = string
  default     = "servicehub"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,14}$", var.name))
    error_message = "Use 2–15 lower-case letters, digits or hyphens, starting with a letter."
  }
}

variable "servicehub_version" {
  description = "The ServiceHub release to run. Never \"latest\"."
  type        = string
  default     = "4.2.0"
}

variable "image" {
  description = "Container image to run. Leave empty for the published image of servicehub_version; if that cannot be pulled, the VM builds that version from source."
  type        = string
  default     = ""
}

variable "vm_size" {
  description = "EC2 instance type. 2 CPUs and 4 GB is comfortable for one team."
  type        = string
  default     = "t3.medium"
}

variable "data_disk_gb" {
  description = "Size of the volume that holds ServiceHub's database and backups."
  type        = number
  default     = 32
}

variable "messaging_resources" {
  description = "ARNs of the SQS queues ServiceHub may read and replay — include each queue AND its dead-letter queue. SNS topic ARNs may be listed too. Empty = no access to any queue."
  type        = list(string)
  default     = []
}

variable "allow_replay" {
  description = "false = read-only: ServiceHub can see dead letters but cannot send or delete anything."
  type        = bool
  default     = true
}

variable "snapshot_retention_days" {
  description = "How many daily snapshots of the data volume to keep."
  type        = number
  default     = 7
}

variable "backup_retention_days" {
  description = "How long copied backups are kept in the bucket."
  type        = number
  default     = 30
}

variable "existing_network" {
  description = "Use a subnet that already exists instead of creating a small VPC. The subnet must be able to reach the internet (for the image and AWS's own endpoints)."
  type        = object({ vpc_id = string, subnet_id = string })
  default     = null
}

variable "tags" {
  description = "Extra tags for every resource."
  type        = map(string)
  default     = {}
}
