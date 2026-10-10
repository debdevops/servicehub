# The key that encrypts every cloud credential ServiceHub stores. Made here, kept in Key Vault, read by the VM's own identity.
# It is never an output and never printed. It IS held in the Terraform state file — keep that file private.
resource "random_bytes" "encryption_key" {
  length = 32
}

resource "azurerm_key_vault" "this" {
  name                       = "kv-${substr(var.name, 0, 10)}-${local.unique}"
  resource_group_name        = azurerm_resource_group.this.name
  location                   = azurerm_resource_group.this.location
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = local.tags
}

# Whoever runs Terraform writes the secret once.
resource "azurerm_role_assignment" "deployer_writes_key" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "vm_reads_key" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.this.principal_id
}

resource "azurerm_key_vault_secret" "encryption_key" {
  name         = "servicehub-encryption-key"
  value        = random_bytes.encryption_key.hex
  key_vault_id = azurerm_key_vault.this.id
  tags         = local.tags

  depends_on = [azurerm_role_assignment.deployer_writes_key]
}
