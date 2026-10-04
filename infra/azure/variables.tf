variable "subscription_id" {
  description = "The Azure subscription to create ServiceHub in."
  type        = string
}

variable "region" {
  description = "Azure location, for example westeurope. Pick the one closest to your Service Bus namespaces."
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
  default     = "4.1.0"
}

variable "image" {
  description = "Container image to run. Leave empty for the published image of servicehub_version; if that cannot be pulled, the VM builds that version from source."
  type        = string
  default     = ""
}

variable "vm_size" {
  description = "VM size. 2 CPUs and 4 GB is comfortable for one team."
  type        = string
  default     = "Standard_B2s"
}

variable "data_disk_gb" {
  description = "Size of the disk that holds ServiceHub's database and backups."
  type        = number
  default     = 32
}

variable "ssh_public_key" {
  description = "Your SSH public key (the text of ~/.ssh/id_ed25519.pub or similar). The VM is reached only through an SSH tunnel."
  type        = string
}

variable "allowed_ssh_cidr" {
  description = "The only address range allowed to open SSH, for example 203.0.113.7/32. Never 0.0.0.0/0."
  type        = string

  validation {
    condition     = can(cidrhost(var.allowed_ssh_cidr, 0)) && var.allowed_ssh_cidr != "0.0.0.0/0"
    error_message = "Give one address range in CIDR form, and not 0.0.0.0/0."
  }
}

variable "messaging_resources" {
  description = "Resource IDs of the Service Bus namespaces ServiceHub will watch. Recorded as a tag only: ServiceHub 4.1.0 connects to Azure with a connection string you paste in the app, not with this VM's identity (see README)."
  type        = list(string)
  default     = []
}

variable "allow_replay" {
  description = "Reserved for when ServiceHub can use this VM's identity on Azure. Has no effect in 4.1.0."
  type        = bool
  default     = true
}

variable "snapshot_retention_days" {
  description = "How many daily snapshots of the data disk to keep."
  type        = number
  default     = 7
}

variable "backup_retention_days" {
  description = "How long copied backups are kept in the storage account."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra tags for every resource."
  type        = map(string)
  default     = {}
}
