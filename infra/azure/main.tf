# ServiceHub on Azure: one small VM, one data disk, the published container, and no way in from the internet except SSH from
# one address. People reach ServiceHub through an SSH tunnel — ServiceHub 4.1.0 has no sign-in, so its port is never exposed.

locals {
  tags  = merge(var.tags, { app = "servicehub" })
  image = var.image != "" ? var.image : "ghcr.io/debdevops/servicehub:${var.servicehub_version}"
  # Storage account and Key Vault names must be globally unique, short and plain.
  unique = substr(sha1("${var.subscription_id}/${var.name}/${var.region}"), 0, 8)
}

data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "this" {
  name     = "rg-${var.name}"
  location = var.region
  tags     = local.tags
}

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  address_space       = ["10.60.0.0/24"]
  tags                = local.tags
}

resource "azurerm_subnet" "this" {
  name                 = "snet-${var.name}"
  resource_group_name  = azurerm_resource_group.this.name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = ["10.60.0.0/27"]
}

# The only inbound rule: SSH, from one address. There is deliberately no rule for ServiceHub's own port.
resource "azurerm_network_security_group" "this" {
  name                = "nsg-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tags                = local.tags

  security_rule {
    name                       = "ssh-from-you"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "22"
    source_address_prefix      = var.allowed_ssh_cidr
    destination_address_prefix = "*"
  }
}

resource "azurerm_subnet_network_security_group_association" "this" {
  subnet_id                 = azurerm_subnet.this.id
  network_security_group_id = azurerm_network_security_group.this.id
}

resource "azurerm_public_ip" "this" {
  name                = "pip-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  allocation_method   = "Static"
  sku                 = "Standard"
  tags                = local.tags
}

resource "azurerm_network_interface" "this" {
  name                = "nic-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tags                = local.tags

  ip_configuration {
    name                          = "primary"
    subnet_id                     = azurerm_subnet.this.id
    private_ip_address_allocation = "Dynamic"
    public_ip_address_id          = azurerm_public_ip.this.id
  }
}

# The identity the VM uses to read its own encryption key and write its backups. Nothing else.
resource "azurerm_user_assigned_identity" "this" {
  name                = "id-${var.name}"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  tags                = local.tags
}

resource "azurerm_linux_virtual_machine" "this" {
  name                            = "vm-${var.name}"
  resource_group_name             = azurerm_resource_group.this.name
  location                        = azurerm_resource_group.this.location
  size                            = var.vm_size
  admin_username                  = "servicehub"
  disable_password_authentication = true
  network_interface_ids           = [azurerm_network_interface.this.id]
  tags                            = merge(local.tags, { servicehub-watches = join(" ", [for id in var.messaging_resources : element(split("/", id), length(split("/", id)) - 1)]) })

  admin_ssh_key {
    username   = "servicehub"
    public_key = var.ssh_public_key
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.this.id]
  }

  os_disk {
    caching              = "ReadWrite"
    storage_account_type = "StandardSSD_LRS"
  }

  source_image_reference {
    publisher = "Canonical"
    offer     = "ubuntu-24_04-lts"
    sku       = "server"
    version   = "latest"
  }

  custom_data = base64encode(templatefile("${path.module}/../shared/startup.sh.tftpl", {
    servicehub_version = var.servicehub_version
    image              = local.image
    source_repo        = "https://github.com/debdevops/servicehub.git"
    read_key_command   = <<-EOT
      TOKEN="$(curl -s -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net&client_id=${azurerm_user_assigned_identity.this.client_id}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
      curl -s -H "Authorization: Bearer $TOKEN" "${azurerm_key_vault.this.vault_uri}secrets/${azurerm_key_vault_secret.encryption_key.name}?api-version=7.4" | python3 -c 'import json,sys; print(json.load(sys.stdin)["value"])'
    EOT
    upload_command     = <<-EOT
      TOKEN="$(curl -s -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fstorage.azure.com%2F&client_id=${azurerm_user_assigned_identity.this.client_id}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')"
      curl -fsS -X PUT -H "Authorization: Bearer $TOKEN" -H "x-ms-version: 2023-11-03" -H "x-ms-blob-type: BlockBlob" --data-binary "@$1" "${azurerm_storage_account.backups.primary_blob_endpoint}${azurerm_storage_container.backups.name}/$2" >/dev/null
    EOT
  }))

  # The key must be readable before the VM first boots and asks for it.
  depends_on = [azurerm_role_assignment.vm_reads_key, azurerm_role_assignment.vm_writes_backups]

  lifecycle {
    # A changed start-up script must not replace a running VM. Update ServiceHub with the steps in the README.
    ignore_changes = [custom_data]
  }
}

resource "azurerm_managed_disk" "data" {
  name                 = "disk-${var.name}-data"
  resource_group_name  = azurerm_resource_group.this.name
  location             = azurerm_resource_group.this.location
  storage_account_type = "StandardSSD_LRS"
  create_option        = "Empty"
  disk_size_gb         = var.data_disk_gb
  tags                 = local.tags
}

resource "azurerm_virtual_machine_data_disk_attachment" "data" {
  managed_disk_id    = azurerm_managed_disk.data.id
  virtual_machine_id = azurerm_linux_virtual_machine.this.id
  lun                = 0
  caching            = "ReadWrite"
}
