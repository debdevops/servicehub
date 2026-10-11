output "tunnel_command" {
  description = "Run this on your own computer and leave it running. It forwards localhost:8080 to ServiceHub. Needs the AWS CLI's Session Manager plugin."
  value       = "aws ssm start-session --region ${var.region} --target ${aws_instance.this.id} --document-name AWS-StartPortForwardingSession --parameters portNumber=8080,localPortNumber=8080"
}

output "url" {
  description = "Open this once the tunnel is running."
  value       = "http://localhost:8080"
}

output "identity" {
  description = "The role ServiceHub runs as. In \"Add a cloud\", choose to use this server's identity — no access key is typed."
  value       = aws_iam_role.this.arn
}

output "secret_location" {
  description = "Where the encryption key is kept. The value is never shown."
  value       = "Secrets Manager secret ${aws_secretsmanager_secret.encryption_key.name}"
}

output "backup_location" {
  description = "Where ServiceHub's backups are copied."
  value       = "s3://${aws_s3_bucket.backups.bucket}"
}

output "kept_on_destroy" {
  description = "What `deploy.sh aws --destroy` leaves behind (use --purge to remove these too). A plain `terraform destroy` removes everything."
  value       = ["the data volume ${aws_ebs_volume.data.id} and its snapshots", "the backups in ${aws_s3_bucket.backups.bucket}", "the encryption key ${aws_secretsmanager_secret.encryption_key.name}"]
}

output "kept_addresses" {
  description = "For the deploy wrappers: the resources a keeping destroy takes out of Terraform's hands first."
  value = [
    "aws_ebs_volume.data", "random_bytes.encryption_key", "aws_secretsmanager_secret.encryption_key", "aws_secretsmanager_secret_version.encryption_key",
    "aws_s3_bucket.backups", "aws_s3_bucket_public_access_block.backups", "aws_s3_bucket_versioning.backups", "aws_s3_bucket_server_side_encryption_configuration.backups", "aws_s3_bucket_lifecycle_configuration.backups",
  ]
}

output "list_command" {
  description = "Lists everything in the region that carries the ServiceHub tag."
  value       = "aws resourcegroupstaggingapi get-resources --region ${var.region} --tag-filters Key=app,Values=servicehub --query \"ResourceTagMappingList[].ResourceARN\" --output table"
}

output "next_steps" {
  description = "What to do now."
  value       = <<-EOT
    1. Wait about five minutes for the first start (longer if the instance has to build ServiceHub from source).
    2. Run:  aws ssm start-session --region ${var.region} --target ${aws_instance.this.id} --document-name AWS-StartPortForwardingSession --parameters portNumber=8080,localPortNumber=8080
    3. Open  http://localhost:8080  and choose "Add a cloud" -> AWS -> use this server's identity, region ${var.region}.
  EOT
}
