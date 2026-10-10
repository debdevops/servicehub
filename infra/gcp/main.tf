# ServiceHub on Google Cloud: one small VM with NO external address, one data disk, the published container.
# People reach ServiceHub through an IAP tunnel (`gcloud compute ssh --tunnel-through-iap`).
# ServiceHub 4.1.0 has no sign-in, so its port is never exposed.

locals {
  image = var.image != "" ? var.image : "ghcr.io/debdevops/servicehub:${var.servicehub_version}"
}

resource "google_project_service" "required" {
  for_each = toset(["compute.googleapis.com", "secretmanager.googleapis.com", "iap.googleapis.com", "pubsub.googleapis.com"])

  service            = each.value
  disable_on_destroy = false
}

resource "google_compute_network" "this" {
  name                    = var.name
  auto_create_subnetworks = false

  depends_on = [google_project_service.required]
}

resource "google_compute_subnetwork" "this" {
  name                     = var.name
  network                  = google_compute_network.this.id
  region                   = var.region
  ip_cidr_range            = "10.60.0.0/27"
  private_ip_google_access = true
}

# The VM has no external address. It reaches out (for the image) through Cloud NAT; nothing can reach in.
resource "google_compute_router" "this" {
  name    = var.name
  network = google_compute_network.this.id
  region  = var.region
}

resource "google_compute_router_nat" "this" {
  name                               = var.name
  router                             = google_compute_router.this.name
  region                             = var.region
  nat_ip_allocate_option             = "AUTO_ONLY"
  source_subnetwork_ip_ranges_to_nat = "ALL_SUBNETWORKS_ALL_IP_RANGES"
}

# The only inbound rule: SSH from Google's Identity-Aware Proxy range, which carries the tunnel. Nothing from the internet.
resource "google_compute_firewall" "iap_ssh" {
  name          = "${var.name}-iap-ssh"
  network       = google_compute_network.this.id
  direction     = "INGRESS"
  source_ranges = ["35.235.240.0/20"]
  target_tags   = [var.name]

  allow {
    protocol = "tcp"
    ports    = ["22"]
  }
}

resource "google_service_account" "this" {
  account_id   = var.name
  display_name = "ServiceHub"
  description  = "The identity ServiceHub's VM runs as."

  depends_on = [google_project_service.required]
}

resource "google_compute_disk" "data" {
  name = "${var.name}-data"
  type = "pd-balanced"
  zone = var.zone
  size = var.data_disk_gb

  depends_on = [google_project_service.required]
}

resource "google_compute_instance" "this" {
  name                      = var.name
  machine_type              = var.vm_size
  zone                      = var.zone
  tags                      = [var.name]
  allow_stopping_for_update = true

  boot_disk {
    initialize_params {
      image = "debian-cloud/debian-12"
      size  = 30
    }
  }

  attached_disk {
    source      = google_compute_disk.data.id
    device_name = "servicehub-data"
  }

  network_interface {
    subnetwork = google_compute_subnetwork.this.id
    # No access_config block: no external address.
  }

  service_account {
    email  = google_service_account.this.email
    scopes = ["cloud-platform"] # what it may actually do is set by the roles in access.tf, not by this scope
  }

  shielded_instance_config {
    enable_secure_boot = true
  }

  metadata = {
    enable-oslogin = "TRUE"
  }

  metadata_startup_script = templatefile("${path.module}/../shared/startup.sh.tftpl", {
    servicehub_version = var.servicehub_version
    image              = local.image
    source_repo        = "https://github.com/debdevops/servicehub.git"
    read_key_command   = <<-EOT
      TOKEN="$(curl -s -H 'Metadata-Flavor: Google' 'http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token' | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
      curl -s -H "Authorization: Bearer $TOKEN" "https://secretmanager.googleapis.com/v1/${google_secret_manager_secret.encryption_key.id}/versions/latest:access" | python3 -c 'import base64,json,sys; print(base64.b64decode(json.load(sys.stdin)["payload"]["data"]).decode())'
    EOT
    upload_command     = <<-EOT
      TOKEN="$(curl -s -H 'Metadata-Flavor: Google' 'http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token' | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
      curl -fsS -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/octet-stream" --data-binary "@$1" "https://storage.googleapis.com/upload/storage/v1/b/${google_storage_bucket.backups.name}/o?uploadType=media&name=$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$2")" >/dev/null
    EOT
  })

  depends_on = [google_compute_router_nat.this, google_secret_manager_secret_version.encryption_key, google_secret_manager_secret_iam_member.vm_reads_key]

  lifecycle {
    ignore_changes = [metadata_startup_script, boot_disk[0].initialize_params[0].image]
  }
}
