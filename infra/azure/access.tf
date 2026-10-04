# What ServiceHub may reach in YOUR Service Bus.
#
# ServiceHub 4.1.0 connects to Azure Service Bus with a connection string that you paste into "Add a cloud" — its Azure
# adapter does not use a managed identity yet. So this module grants the VM's identity NOTHING on your namespaces: giving
# a role that is not used would only widen access for no benefit. When ServiceHub gains identity-based access to Azure,
# the role assignments belong in this file, limited to var.messaging_resources.
#
# What you need instead: in each namespace, a Shared Access Policy for ServiceHub — see docs/clouds/azure.md.
