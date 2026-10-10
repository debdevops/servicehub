# Two safety nets: a daily snapshot of the data disk, and each ServiceHub backup copied to a private bucket.

resource "random_id" "bucket" {
  byte_length = 4
}

resource "google_storage_bucket" "backups" {
  name                        = "${var.name}-backups-${random_id.bucket.hex}"
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  # Lets a purge remove the bucket even though it holds backups. A keeping destroy never reaches this (see deploy.sh).
  force_destroy = true

  versioning {
    enabled = true
  }

  lifecycle_rule {
    condition {
      age = var.backup_retention_days
    }
    action {
      type = "Delete"
    }
  }
}

# The VM may add backups. It cannot read, list or delete them.
resource "google_storage_bucket_iam_member" "vm_writes_backups" {
  bucket = google_storage_bucket.backups.name
  role   = "roles/storage.objectCreator"
  member = "serviceAccount:${google_service_account.this.email}"
}

resource "google_compute_resource_policy" "daily_snapshot" {
  name   = "${var.name}-daily-snapshot"
  region = var.region

  snapshot_schedule_policy {
    schedule {
      daily_schedule {
        days_in_cycle = 1
        start_time    = "02:00"
      }
    }
    retention_policy {
      max_retention_days    = var.snapshot_retention_days
      on_source_disk_delete = "KEEP_AUTO_SNAPSHOTS"
    }
  }

  depends_on = [google_project_service.required]
}

resource "google_compute_disk_resource_policy_attachment" "data" {
  name = google_compute_resource_policy.daily_snapshot.name
  disk = google_compute_disk.data.name
  zone = var.zone
}
