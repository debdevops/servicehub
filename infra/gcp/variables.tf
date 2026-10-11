variable "project_id" {
  description = "The Google Cloud project to create ServiceHub in — normally the one that holds your Pub/Sub subscriptions."
  type        = string
}

variable "region" {
  description = "Region, for example europe-west1."
  type        = string
}

variable "zone" {
  description = "Zone inside that region, for example europe-west1-b."
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
  description = "Machine type. 2 CPUs and 4 GB is comfortable for one team."
  type        = string
  default     = "e2-medium"
}

variable "data_disk_gb" {
  description = "Size of the disk that holds ServiceHub's database and backups."
  type        = number
  default     = 32
}

variable "messaging_resources" {
  description = "IDs (short names) of the Pub/Sub subscriptions in this project that ServiceHub may read and replay — include each subscription AND its dead-letter subscription. Empty = no access to any message."
  type        = list(string)
  default     = []
}

variable "replay_topics" {
  description = "IDs (short names) of the topics ServiceHub may publish replayed messages to. Used only when allow_replay is true."
  type        = list(string)
  default     = []
}

variable "allow_replay" {
  description = "false = read-only: ServiceHub can see dead letters but cannot publish anything."
  type        = bool
  default     = true
}

variable "snapshot_retention_days" {
  description = "How many daily snapshots of the data disk to keep."
  type        = number
  default     = 7
}

variable "backup_retention_days" {
  description = "How long copied backups are kept in the bucket."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Extra labels for every resource (lower-case keys and values)."
  type        = map(string)
  default     = {}
}
