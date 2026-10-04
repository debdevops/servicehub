# Two safety nets: a daily snapshot of the data disk, and each ServiceHub backup copied to a private storage account.

resource "azurerm_storage_account" "backups" {
  name                            = "st${replace(substr(var.name, 0, 10), "-", "")}${local.unique}"
  resource_group_name             = azurerm_resource_group.this.name
  location                        = azurerm_resource_group.this.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false
  tags                            = local.tags

  blob_properties {
    versioning_enabled = true
  }
}

resource "azurerm_storage_container" "backups" {
  name                  = "servicehub-backups"
  storage_account_id    = azurerm_storage_account.backups.id
  container_access_type = "private"
}

resource "azurerm_role_assignment" "vm_writes_backups" {
  scope                = azurerm_storage_account.backups.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_storage_management_policy" "backups" {
  storage_account_id = azurerm_storage_account.backups.id

  rule {
    name    = "expire-old-backups"
    enabled = true
    filters {
      blob_types = ["blockBlob"]
    }
    actions {
      base_blob {
        delete_after_days_since_modification_greater_than = var.backup_retention_days
      }
      version {
        delete_after_days_since_creation = var.backup_retention_days
      }
    }
  }
}

resource "azurerm_snapshot" "initial" {
  name                = "snap-${var.name}-data-initial"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  create_option       = "Copy"
  source_uri          = azurerm_managed_disk.data.id
  tags                = local.tags
}

# Daily snapshots of the data disk, kept for snapshot_retention_days.
resource "azurerm_data_protection_backup_vault" "this" {
  name                = "bv-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  datastore_type      = "VaultStore"
  redundancy          = "LocallyRedundant"
  tags                = local.tags

  identity {
    type = "SystemAssigned"
  }
}

resource "azurerm_role_assignment" "vault_reads_disk" {
  scope                = azurerm_managed_disk.data.id
  role_definition_name = "Disk Backup Reader"
  principal_id         = azurerm_data_protection_backup_vault.this.identity[0].principal_id
}

resource "azurerm_role_assignment" "vault_writes_snapshots" {
  scope                = azurerm_resource_group.this.id
  role_definition_name = "Disk Snapshot Contributor"
  principal_id         = azurerm_data_protection_backup_vault.this.identity[0].principal_id
}

resource "azurerm_data_protection_backup_policy_disk" "daily" {
  name                            = "daily"
  vault_id                        = azurerm_data_protection_backup_vault.this.id
  backup_repeating_time_intervals = ["R/2024-01-01T02:00:00+00:00/P1D"]
  default_retention_duration      = "P${var.snapshot_retention_days}D"
}

resource "azurerm_data_protection_backup_instance_disk" "data" {
  name                         = "data-disk"
  location                     = azurerm_resource_group.this.location
  vault_id                     = azurerm_data_protection_backup_vault.this.id
  disk_id                      = azurerm_managed_disk.data.id
  snapshot_resource_group_name = azurerm_resource_group.this.name
  backup_policy_id             = azurerm_data_protection_backup_policy_disk.daily.id

  depends_on = [azurerm_role_assignment.vault_reads_disk, azurerm_role_assignment.vault_writes_snapshots]
}
