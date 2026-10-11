# What ServiceHub may do — and only on what you named.
#
# In ServiceHub, connect Google Cloud with "Use this server's identity": no service-account key file is ever made or typed.

# Listing names of topics and subscriptions in the project. It shows names and settings, never messages.
resource "google_project_iam_member" "list_names" {
  project = var.project_id
  role    = "roles/pubsub.viewer"
  member  = "serviceAccount:${google_service_account.this.email}"
}

# Reading (pull, acknowledge) — only on the subscriptions you named.
resource "google_pubsub_subscription_iam_member" "read" {
  for_each = toset(var.messaging_resources)

  subscription = each.value
  role         = "roles/pubsub.subscriber"
  member       = "serviceAccount:${google_service_account.this.email}"
}

# Replaying publishes the message back to its topic — only on the topics you named, and only if replay is allowed.
resource "google_pubsub_topic_iam_member" "replay" {
  for_each = var.allow_replay ? toset(var.replay_topics) : toset([])

  topic  = each.value
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.this.email}"
}
