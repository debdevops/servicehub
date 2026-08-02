# AWS Infrastructure Setup Guide for ServiceHub

This guide explains how to create AWS messaging infrastructure, test it, and connect it to ServiceHub.

It is written for beginners. Follow the AWS Console steps first, then repeat the same work with AWS CLI when you want a repeatable setup.

---

## What You Will Build

This guide helps you create:

- an IAM identity for testing
- an SQS queue
- an SQS dead-letter queue
- a redrive policy
- an SNS topic
- an SNS to SQS subscription

At the end, you will be able to:

- provision AWS messaging infrastructure from the console
- provision the same infrastructure through AWS CLI
- send and receive test messages
- connect AWS details to ServiceHub

---

## Before You Start

Make sure you have:

1. an AWS account
2. permission to create IAM, SQS, and SNS resources
3. ServiceHub running locally or deployed
4. a non-production environment such as `dev` or `uat`

Recommended approach:

- begin with a dedicated IAM user or role
- use a non-production account or sandbox if possible
- test in one region first, such as `us-east-1`
- avoid production until the workflow is proven

---

## How AWS Works in ServiceHub

AWS SQS/SNS is **preview** — implemented and unit-tested, not validated against live AWS services
in this project's own CI, capability-gated (see [docs/PROVIDER-SUPPORT.md](../../PROVIDER-SUPPORT.md)
for the full matrix), and with no feature-parity guarantee against Azure. This is a deliberate,
permanent maturity label for this provider, not a temporary rollout phase.

You can enter:

- display name
- AWS Access Key ID
- AWS Secret Access Key
- AWS region
- optional queue or topic prefix
- environment tag

Important note:

- ServiceHub includes AWS-oriented UI flows.
- The Connect page labels AWS as preview.
- AWS is disabled by default on the server (`CloudProviders:Aws:Enabled=false`) — an operator must explicitly enable it.

---

## AWS Architecture You Will Create

- IAM user or role with SQS and SNS permissions
- SQS queue
- SQS dead-letter queue
- redrive policy
- SNS topic
- optional SNS subscription to SQS

---

## AWS Prerequisites

Install AWS CLI:

```bash
aws --version
```

Configure credentials:

```bash
aws configure
```

You will be asked for:

- AWS Access Key ID
- AWS Secret Access Key
- default region, for example `us-east-1`
- output format, for example `json`

Recommended for learning:

- create a dedicated IAM user for ServiceHub testing
- do not use a root account

---

## AWS Setup Through the AWS Console UI

### Step 1: Create an IAM User for Testing

1. Open the AWS Console.
2. Search for `IAM`.
3. Click `Users`.
4. Click `Create user`.
5. Name it something like `servicehub-dev-user`.
6. Create programmatic access credentials if your workflow requires access keys.

Attach policies carefully.

For a beginner sandbox, start with a small custom policy or restricted SQS and SNS permissions.

Typical permissions:

- `sqs:GetQueueAttributes`
- `sqs:GetQueueUrl`
- `sqs:ListQueues`
- `sqs:ReceiveMessage`
- `sqs:SendMessage`
- `sqs:DeleteMessage`
- `sns:ListTopics`
- `sns:Publish`
- `sns:Subscribe`

### Step 2: Create a Dead-Letter Queue

1. Search for `SQS`.
2. Click `Create queue`.
3. Choose `Standard` unless you specifically need FIFO behavior.
4. Name it `orders-dlq`.
5. Click `Create queue`.

### Step 3: Create the Main Queue

1. Create another queue named `orders`.
2. In the queue settings, configure the dead-letter queue.
3. Choose `orders-dlq`.
4. Set a `maxReceiveCount`, for example `5`.
5. Create the queue.

### Step 4: Create an SNS Topic

1. Search for `SNS`.
2. Click `Topics`.
3. Click `Create topic`.
4. Choose `Standard`.
5. Name it `orders-events`.
6. Create the topic.

### Step 5: Subscribe the Queue to the Topic

1. Open the SNS topic.
2. Click `Create subscription`.
3. Protocol: `Amazon SQS`.
4. Endpoint: choose the `orders` queue ARN.
5. Create the subscription.

You may need to allow the SNS topic to publish to the queue by updating the queue access policy. The console often helps you do this automatically.

---

## AWS Setup Through AWS CLI

Set variables:

```bash
REGION="us-east-1"
MAIN_QUEUE="orders"
DLQ_QUEUE="orders-dlq"
TOPIC_NAME="orders-events"
```

### Step 1: Create the Dead-Letter Queue

```bash
aws sqs create-queue \
  --queue-name "$DLQ_QUEUE" \
  --region "$REGION"
```

Get its URL and ARN:

```bash
DLQ_URL=$(aws sqs get-queue-url --queue-name "$DLQ_QUEUE" --region "$REGION" --query QueueUrl --output text)
DLQ_ARN=$(aws sqs get-queue-attributes --queue-url "$DLQ_URL" --attribute-names QueueArn --region "$REGION" --query Attributes.QueueArn --output text)
```

### Step 2: Create the Main Queue with Redrive Policy

```bash
aws sqs create-queue \
  --queue-name "$MAIN_QUEUE" \
  --attributes RedrivePolicy='{"deadLetterTargetArn":"'"$DLQ_ARN"'","maxReceiveCount":"5"}' \
  --region "$REGION"
```

Get the main queue URL and ARN:

```bash
MAIN_URL=$(aws sqs get-queue-url --queue-name "$MAIN_QUEUE" --region "$REGION" --query QueueUrl --output text)
MAIN_ARN=$(aws sqs get-queue-attributes --queue-url "$MAIN_URL" --attribute-names QueueArn --region "$REGION" --query Attributes.QueueArn --output text)
```

### Step 3: Create the SNS Topic

```bash
TOPIC_ARN=$(aws sns create-topic \
  --name "$TOPIC_NAME" \
  --region "$REGION" \
  --query TopicArn \
  --output text)
```

### Step 4: Allow the Topic to Publish to the Queue

Create a policy file called `sqs-policy.json`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "Allow-SNS-SendMessage",
      "Effect": "Allow",
      "Principal": {
        "Service": "sns.amazonaws.com"
      },
      "Action": "sqs:SendMessage",
      "Resource": "MAIN_QUEUE_ARN",
      "Condition": {
        "ArnEquals": {
          "aws:SourceArn": "TOPIC_ARN"
        }
      }
    }
  ]
}
```

Replace `MAIN_QUEUE_ARN` and `TOPIC_ARN` with real values.

Apply it:

```bash
aws sqs set-queue-attributes \
  --queue-url "$MAIN_URL" \
  --attributes Policy="$(cat sqs-policy.json)" \
  --region "$REGION"
```

### Step 5: Subscribe the Queue to the Topic

```bash
aws sns subscribe \
  --topic-arn "$TOPIC_ARN" \
  --protocol sqs \
  --notification-endpoint "$MAIN_ARN" \
  --region "$REGION"
```

---

## How to Test AWS Infrastructure

### Send directly to SQS

```bash
aws sqs send-message \
  --queue-url "$MAIN_URL" \
  --message-body '{"orderId":"1001","status":"Created"}' \
  --region "$REGION"
```

### Receive the message from SQS

```bash
aws sqs receive-message \
  --queue-url "$MAIN_URL" \
  --max-number-of-messages 1 \
  --region "$REGION"
```

### Publish through SNS

```bash
aws sns publish \
  --topic-arn "$TOPIC_ARN" \
  --message '{"orderId":"2001","event":"OrderCreated"}' \
  --region "$REGION"
```

Then receive again from SQS to confirm the fanout path works.

### Test dead-letter behavior

An SQS message goes to the dead-letter queue after repeated failed processing attempts. That usually happens in your consumer application, not just by provisioning queues.

For a beginner lab, it is enough to:

- confirm the DLQ is attached
- confirm the redrive policy exists
- confirm your consumer moves failed messages after enough receives

---

## How to Connect AWS to ServiceHub

1. Open ServiceHub.
2. Go to `Connect`.
3. Select `AWS`.
4. Enter a display name such as `AWS Dev SQS`.
5. Enter the AWS Access Key ID.
6. Enter the AWS Secret Access Key.
7. Choose the region.
8. Optionally enter a queue or topic prefix if you want to filter resources.
9. Choose the environment.
10. Save the connection.

What to expect in the current release:

- the UI supports AWS connection details
- AWS appears in multi-cloud and cloud-bridge related flows
- AWS remains preview regardless of configuration — see [docs/PROVIDER-SUPPORT.md](../../PROVIDER-SUPPORT.md) for exactly what that means and the full capability matrix

Best practice:

- verify `CloudProviders:Aws:Enabled` is set in your deployed environment before onboarding production namespaces

---

## AWS Security Advice for Beginners

- Use a dedicated IAM user or role.
- Do not use your root account.
- Start with least privilege.
- Restrict access to the exact queues, topics, and region you need.
- Rotate access keys regularly.

---

## AWS Checklist

- IAM identity created
- SQS queue created
- dead-letter queue created
- SNS topic created
- SNS to SQS subscription configured
- test message sent and received
- ServiceHub AWS connection saved