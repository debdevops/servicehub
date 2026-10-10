output "tunnel_command" {
  description = "Run this on your own computer and leave it running. It forwards localhost:8080 to ServiceHub."
  value       = "ssh -N -L 8080:127.0.0.1:8080 servicehub@${azurerm_public_ip.this.ip_address}"
}

output "url" {
  description = "Open this once the tunnel is running."
  value       = "http://localhost:8080"
}

output "identity" {
  description = "The identity the VM runs as (used only for its own key and backups on Azure)."
  value       = azurerm_user_assigned_identity.this.name
}

output "secret_location" {
  description = "Where the encryption key is kept. The value is never shown."
  value       = "Key Vault ${azurerm_key_vault.this.name}, secret ${azurerm_key_vault_secret.encryption_key.name}"
}

output "backup_location" {
  description = "Where ServiceHub's backups are copied."
  value       = "Storage account ${azurerm_storage_account.backups.name}, container ${azurerm_storage_container.backups.name}"
}

output "kept_on_destroy" {
  description = "What `deploy.sh azure --destroy` leaves behind (use --purge to remove these too). A plain `terraform destroy` removes everything."
  value       = ["the data disk ${azurerm_managed_disk.data.name}", "the backups in ${azurerm_storage_account.backups.name}", "the encryption key in ${azurerm_key_vault.this.name}", "the snapshots in ${azurerm_data_protection_backup_vault.this.name}"]
}

output "kept_addresses" {
  description = "For the deploy wrappers: the resources a keeping destroy takes out of Terraform's hands first."
  value = [
    "azurerm_resource_group.this", "azurerm_managed_disk.data", "random_bytes.encryption_key", "azurerm_key_vault.this", "azurerm_key_vault_secret.encryption_key",
    "azurerm_role_assignment.deployer_writes_key", "azurerm_storage_account.backups", "azurerm_storage_container.backups", "azurerm_storage_management_policy.backups",
    "azurerm_snapshot.initial", "azurerm_data_protection_backup_vault.this", "azurerm_data_protection_backup_policy_disk.daily", "azurerm_data_protection_backup_instance_disk.data",
    "azurerm_role_assignment.vault_reads_disk", "azurerm_role_assignment.vault_writes_snapshots",
  ]
}

output "list_command" {
  description = "Lists everything in the account that carries the ServiceHub tag."
  value       = "az resource list --tag app=servicehub --subscription ${var.subscription_id} --query \"[].{name:name,type:type,group:resourceGroup}\" -o table"
}

output "next_steps" {
  description = "What to do now."
  value       = <<-EOT
    1. Wait about five minutes for the first start (longer if the VM has to build ServiceHub from source).
    2. Run:  ssh -N -L 8080:127.0.0.1:8080 servicehub@${azurerm_public_ip.this.ip_address}
    3. Open  http://localhost:8080  and choose "Add a cloud".
       On Azure, paste a connection string for a Shared Access Policy made for ServiceHub (docs/clouds/azure.md).
  EOT
}
