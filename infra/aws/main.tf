# ServiceHub on AWS: one small EC2 instance, one data volume, the published container, and NO inbound rule at all.
# People reach ServiceHub through Session Manager port forwarding — there is no SSH key and no open port.
# ServiceHub 4.1.0 has no sign-in, so its port is never exposed.

locals {
  image       = var.image != "" ? var.image : "ghcr.io/debdevops/servicehub:${var.servicehub_version}"
  create_vpc  = var.existing_network == null
  vpc_id      = local.create_vpc ? aws_vpc.this[0].id : var.existing_network.vpc_id
  subnet_id   = local.create_vpc ? aws_subnet.this[0].id : var.existing_network.subnet_id
  queue_arns  = [for arn in var.messaging_resources : arn if startswith(arn, "arn:aws:sqs:")]
  topic_arns  = [for arn in var.messaging_resources : arn if startswith(arn, "arn:aws:sns:")]
  name_tagged = { Name = var.name }
}

data "aws_caller_identity" "current" {}

data "aws_vpc" "existing" {
  count = local.create_vpc ? 0 : 1
  id    = var.existing_network.vpc_id
}

data "aws_ssm_parameter" "al2023" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

# A small network of its own. The instance has a public address only so it can reach out (a NAT gateway would cost more than
# the instance); nothing can reach in, because the security group has no inbound rule.
resource "aws_vpc" "this" {
  count                = local.create_vpc ? 1 : 0
  cidr_block           = "10.60.0.0/24"
  enable_dns_support   = true
  enable_dns_hostnames = true
  tags                 = local.name_tagged
}

resource "aws_internet_gateway" "this" {
  count  = local.create_vpc ? 1 : 0
  vpc_id = aws_vpc.this[0].id
  tags   = local.name_tagged
}

resource "aws_subnet" "this" {
  count                   = local.create_vpc ? 1 : 0
  vpc_id                  = aws_vpc.this[0].id
  cidr_block              = "10.60.0.0/27"
  map_public_ip_on_launch = true
  tags                    = local.name_tagged
}

resource "aws_route_table" "this" {
  count  = local.create_vpc ? 1 : 0
  vpc_id = aws_vpc.this[0].id
  tags   = local.name_tagged

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this[0].id
  }
}

resource "aws_route_table_association" "this" {
  count          = local.create_vpc ? 1 : 0
  subnet_id      = aws_subnet.this[0].id
  route_table_id = aws_route_table.this[0].id
}

# No inbound rule. Outbound only, on HTTPS — to AWS's own endpoints and to fetch the image.
resource "aws_security_group" "this" {
  name        = var.name
  description = "ServiceHub: nothing in, HTTPS out"
  vpc_id      = local.vpc_id
  tags        = local.name_tagged

  egress {
    description = "HTTPS out"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  # Name lookups go to the VPC resolver (the network's .2 address, inside the VPC range). Without this the instance cannot
  # resolve SSM, GHCR or the AWS endpoints, and first boot fails. Not open to the internet.
  egress {
    description = "DNS (UDP) to the VPC resolver"
    from_port   = 53
    to_port     = 53
    protocol    = "udp"
    cidr_blocks = [local.create_vpc ? aws_vpc.this[0].cidr_block : data.aws_vpc.existing[0].cidr_block]
  }

  egress {
    description = "DNS (TCP) to the VPC resolver"
    from_port   = 53
    to_port     = 53
    protocol    = "tcp"
    cidr_blocks = [local.create_vpc ? aws_vpc.this[0].cidr_block : data.aws_vpc.existing[0].cidr_block]
  }
}

resource "aws_iam_role" "this" {
  name = "${var.name}-instance"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

# Lets people open a Session Manager tunnel. This is AWS's own minimal policy for that.
resource "aws_iam_role_policy_attachment" "session_manager" {
  role       = aws_iam_role.this.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "this" {
  name = "${var.name}-instance"
  role = aws_iam_role.this.name
}

resource "aws_instance" "this" {
  ami                    = data.aws_ssm_parameter.al2023.value
  instance_type          = var.vm_size
  subnet_id              = local.subnet_id
  vpc_security_group_ids = [aws_security_group.this.id]
  iam_instance_profile   = aws_iam_instance_profile.this.name
  tags                   = local.name_tagged

  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }

  root_block_device {
    volume_type = "gp3"
    volume_size = 30
    encrypted   = true
  }

  user_data = templatefile("${path.module}/../shared/startup.sh.tftpl", {
    servicehub_version = var.servicehub_version
    image              = local.image
    source_repo        = "https://github.com/debdevops/servicehub.git"
    read_key_command   = "aws secretsmanager get-secret-value --region ${var.region} --secret-id ${aws_secretsmanager_secret.encryption_key.arn} --query SecretString --output text"
    upload_command     = "aws s3 cp --only-show-errors --region ${var.region} \"$1\" \"s3://${aws_s3_bucket.backups.bucket}/$2\""
  })

  depends_on = [aws_iam_role_policy.own_key_and_backups, aws_secretsmanager_secret_version.encryption_key]

  lifecycle {
    # A newer Amazon Linux image or a changed start-up script must not replace a running instance.
    ignore_changes = [ami, user_data]
  }
}

resource "aws_ebs_volume" "data" {
  availability_zone = aws_instance.this.availability_zone
  size              = var.data_disk_gb
  type              = "gp3"
  encrypted         = true
  tags              = { Name = "${var.name}-data", servicehub-data = "true" }
}

resource "aws_volume_attachment" "data" {
  device_name = "/dev/sdf"
  volume_id   = aws_ebs_volume.data.id
  instance_id = aws_instance.this.id
}
