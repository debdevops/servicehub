# The key that encrypts every cloud credential ServiceHub stores. Made here, kept in Secrets Manager, read by the instance's
# own role. It is never an output and never printed. It IS held in the Terraform state file — keep that file private.
resource "random_bytes" "encryption_key" {
  length = 32
}

resource "aws_secretsmanager_secret" "encryption_key" {
  name_prefix             = "${var.name}-encryption-key-"
  description             = "ServiceHub's encryption key. Losing it makes every stored credential unreadable."
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "encryption_key" {
  secret_id     = aws_secretsmanager_secret.encryption_key.id
  secret_string = random_bytes.encryption_key.hex
}

# The instance may read its own key and write its own backups. Nothing else of its own.
data "aws_iam_policy_document" "own_key_and_backups" {
  statement {
    sid       = "ReadOwnKey"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.encryption_key.arn]
  }

  statement {
    sid       = "WriteOwnBackups"
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.backups.arn}/*"]
  }
}

resource "aws_iam_role_policy" "own_key_and_backups" {
  name   = "own-key-and-backups"
  role   = aws_iam_role.this.id
  policy = data.aws_iam_policy_document.own_key_and_backups.json
}
