#!/usr/bin/env bash
# Rebuild the AWS screenshot set from scratch: empty the live orders queue, restart the throw-away instance on an empty DB, capture.
# Needs DOCS_SCRATCH (a scratch dir) and AWS_KEY_FILE (a file holding "<access key id> <secret access key>" of the ServiceHub IAM user).
# The dead-letter queue is left as it is: a run moves a few dead letters back to the orders queue, so it shrinks by that many.
set -euo pipefail
cd "$(dirname "$0")"
: "${AWS_KEY_FILE:?file with the ServiceHub IAM user access key id and secret}"
export AWS_KEY_FILE AWS_REGION="${AWS_REGION:-ap-south-1}"
# The CLI calls below must act as the same IAM user the walkthrough connects with, not whatever profile the shell has.
read -r AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY < "$AWS_KEY_FILE"
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
unset AWS_SESSION_TOKEN AWS_PROFILE
Q="$(aws sqs get-queue-url --region "$AWS_REGION" --queue-name servicehub-dev-orders --query QueueUrl --output text)"
while :; do
  M="$(aws sqs receive-message --region "$AWS_REGION" --queue-url "$Q" --max-number-of-messages 10 --wait-time-seconds 2 --query 'Messages[].ReceiptHandle' --output text)"
  [ -z "$M" ] || [ "$M" = "None" ] && break
  for h in $M; do aws sqs delete-message --region "$AWS_REGION" --queue-url "$Q" --receipt-handle "$h"; done
done
./reset-instance.sh
node aws.spec.mjs
