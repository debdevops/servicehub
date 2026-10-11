output "tunnel_command" {
  description = "Run this on your own computer and leave it running. It forwards localhost:8080 to ServiceHub."
  value       = "gcloud compute ssh ${google_compute_instance.this.name} --project ${var.project_id} --zone ${var.zone} --tunnel-through-iap -- -N -L 8080:127.0.0.1:8080"
}

output "url" {
  description = "Open this once the tunnel is running."
  value       = "http://localhost:8080"
}

output "identity" {
  description = "The service account ServiceHub runs as. In \"Add a cloud\", choose to use this server's identity — no key file is made or typed."
  value       = google_service_account.this.email
}

output "secret_location" {
  description = "Where the encryption key is kept. The value is never shown."
  value       = "Secret Manager secret ${google_secret_manager_secret.encryption_key.secret_id}"
}

output "backup_location" {
  description = "Where ServiceHub's backups are copied."
  value       = "gs://${google_storage_bucket.backups.name}"
}

output "kept_on_destroy" {
  description = "What `deploy.sh gcp --destroy` leaves behind (use --purge to remove these too). A plain `terraform destroy` removes everything."
  value       = ["the data disk ${google_compute_disk.data.name} and its snapshots", "the backups in ${google_storage_bucket.backups.name}", "the encryption key ${google_secret_manager_secret.encryption_key.secret_id}"]
}

output "kept_addresses" {
  description = "For the deploy wrappers: the resources a keeping destroy takes out of Terraform's hands first."
  value = [
    "google_compute_disk.data", "google_compute_disk_resource_policy_attachment.data", "google_compute_resource_policy.daily_snapshot",
    "random_bytes.encryption_key", "google_secret_manager_secret.encryption_key", "google_secret_manager_secret_version.encryption_key",
    "random_id.bucket", "google_storage_bucket.backups",
  ]
}

output "list_command" {
  description = "Lists everything in the project that carries the ServiceHub label."
  value       = "gcloud asset search-all-resources --project ${var.project_id} --query \"labels.app=servicehub\" --format \"table(name,assetType)\""
}

output "next_steps" {
  description = "What to do now."
  value       = <<-EOT
    1. Wait about five minutes for the first start (longer if the VM has to build ServiceHub from source).
    2. Run:  gcloud compute ssh ${google_compute_instance.this.name} --project ${var.project_id} --zone ${var.zone} --tunnel-through-iap -- -N -L 8080:127.0.0.1:8080
    3. Open  http://localhost:8080  and choose "Add a cloud" -> Google Cloud -> use this server's identity, project ${var.project_id}.
  EOT
}
