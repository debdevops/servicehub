# The key that encrypts every cloud credential ServiceHub stores. Made here, kept in Secret Manager, read by the VM's own
# service account. It is never an output and never printed. It IS held in the Terraform state file — keep that file private.
resource "random_bytes" "encryption_key" {
  length = 32
}

resource "google_secret_manager_secret" "encryption_key" {
  secret_id = "${var.name}-encryption-key"

  replication {
    auto {}
  }

  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "encryption_key" {
  secret      = google_secret_manager_secret.encryption_key.id
  secret_data = random_bytes.encryption_key.hex
}

resource "google_secret_manager_secret_iam_member" "vm_reads_key" {
  secret_id = google_secret_manager_secret.encryption_key.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.this.email}"
}
