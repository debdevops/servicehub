# What ServiceHub may do — and only on what you named.
#
# These are exactly the calls ServiceHub's AWS adapter makes (services/api/src/ServiceHub.Providers.Aws).
# In ServiceHub, connect AWS with "Use this server's identity": no access key is ever typed.

data "aws_iam_policy_document" "messaging" {
  # Listing is not limited to named resources by AWS, so it is granted account-wide. It shows names, never messages.
  statement {
    sid       = "ListNames"
    actions   = ["sqs:ListQueues", "sns:ListTopics"]
    resources = ["*"]
  }

  dynamic "statement" {
    for_each = length(local.queue_arns) > 0 ? [1] : []
    content {
      sid       = "ReadNamedQueues"
      actions   = ["sqs:GetQueueUrl", "sqs:GetQueueAttributes", "sqs:ReceiveMessage", "sqs:ChangeMessageVisibility"]
      resources = local.queue_arns
    }
  }

  dynamic "statement" {
    for_each = length(local.queue_arns) > 0 && var.allow_replay ? [1] : []
    content {
      sid       = "ReplayAndPurgeNamedQueues"
      actions   = ["sqs:SendMessage", "sqs:DeleteMessage"]
      resources = local.queue_arns
    }
  }

  dynamic "statement" {
    for_each = length(local.topic_arns) > 0 ? [1] : []
    content {
      sid       = "ReadNamedTopics"
      actions   = ["sns:ListSubscriptionsByTopic"]
      resources = local.topic_arns
    }
  }

  dynamic "statement" {
    for_each = length(local.topic_arns) > 0 && var.allow_replay ? [1] : []
    content {
      sid       = "PublishToNamedTopics"
      actions   = ["sns:Publish"]
      resources = local.topic_arns
    }
  }
}

resource "aws_iam_role_policy" "messaging" {
  name   = "messaging"
  role   = aws_iam_role.this.id
  policy = data.aws_iam_policy_document.messaging.json
}
