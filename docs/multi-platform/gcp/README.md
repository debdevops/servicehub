# GCP Infrastructure Setup Guide for ServiceHub

This guide explains how to create Google Cloud Pub/Sub infrastructure, test it, and connect it to ServiceHub.

It is written for beginners. Follow the Google Cloud Console steps first, then repeat the same work with `gcloud` when you want a repeatable setup.

---

## What You Will Build

This guide helps you create:

- a GCP project or project-scoped setup
- a Pub/Sub topic
- a Pub/Sub subscription
- a service account
- a service account JSON key

At the end, you will be able to:

- provision GCP messaging infrastructure from the console
- provision the same infrastructure through `gcloud`
- publish and pull test messages
- connect GCP details to ServiceHub

---

## Before You Start

Make sure you have:

1. a GCP account and project
2. permission to create Pub/Sub and IAM resources
3. ServiceHub running locally or deployed
4. a non-production environment such as `dev` or `uat`

Recommended approach:

- start in one project only
- use a dedicated service account for ServiceHub
- use small test messages
- avoid production until the workflow is proven

---

## How GCP Works in ServiceHub

GCP Pub/Sub is **preview** — implemented and unit-tested, not validated against live GCP services
in this project's own CI, capability-gated (see [docs/PROVIDER-SUPPORT.md](../../PROVIDER-SUPPORT.md)
for the full matrix), and with no feature-parity guarantee against Azure. This is a deliberate,
permanent maturity label for this provider, not a temporary rollout phase.

You can enter:

- display name
- GCP project ID
- service account JSON
- environment tag

Important note:

- ServiceHub includes GCP-oriented UI flows.
- The Connect page labels GCP as preview.
- GCP is disabled by default on the server (`CloudProviders:Gcp:Enabled=false`) — an operator must explicitly enable it.

---

## GCP Architecture You Will Create

- GCP project
- Pub/Sub topic
- Pub/Sub subscription
- service account
- service account JSON key
- optional dead-letter topic or subscription behavior later

---

## GCP Prerequisites

Install Google Cloud CLI:

```bash
gcloud --version
```

Authenticate:

```bash
gcloud auth login
```

Set your active project:

```bash
gcloud config set project YOUR_PROJECT_ID
```

Enable Pub/Sub if it is not already enabled:

```bash
gcloud services enable pubsub.googleapis.com
```

---

## GCP Setup Through the Google Cloud Console UI

### Step 1: Create or Choose a Project

1. Open Google Cloud Console.
2. In the top header, choose a project or create a new one.
3. Name it something like `servicehub-dev`.

### Step 2: Enable the Pub/Sub API

1. Open `APIs & Services`.
2. Search for `Pub/Sub API`.
3. Click `Enable`.

### Step 3: Create a Topic

1. Search for `Pub/Sub`.
2. Click `Topics`.
3. Click `Create topic`.
4. Name it `orders-topic`.
5. Create it.

### Step 4: Create a Subscription

1. Open the topic.
2. Click `Create subscription`.
3. Name it `orders-subscription`.
4. Delivery type: keep `Pull` for beginner testing.
5. Create it.

### Step 5: Create a Service Account

1. Search for `IAM & Admin`.
2. Click `Service Accounts`.
3. Click `Create service account`.
4. Name it `servicehub-dev-sa`.
5. Grant a role such as `Pub/Sub Subscriber`.

If you want broader testing:

- add `Pub/Sub Viewer`
- add `Pub/Sub Publisher` only if you want send or publish capability from tools that use this identity

### Step 6: Create a JSON Key

1. Open the service account.
2. Go to `Keys`.
3. Click `Add key`.
4. Choose `Create new key`.
5. Select `JSON`.
6. Download the file.

Keep it secure.

---

## GCP Setup Through gcloud CLI

Set variables:

```bash
PROJECT_ID="your-project-id"
TOPIC_NAME="orders-topic"
SUBSCRIPTION_NAME="orders-subscription"
SERVICE_ACCOUNT_NAME="servicehub-dev-sa"
SERVICE_ACCOUNT_EMAIL="$SERVICE_ACCOUNT_NAME@$PROJECT_ID.iam.gserviceaccount.com"
```

### Step 1: Enable the API

```bash
gcloud services enable pubsub.googleapis.com --project "$PROJECT_ID"
```

### Step 2: Create a Topic

```bash
gcloud pubsub topics create "$TOPIC_NAME" --project "$PROJECT_ID"
```

### Step 3: Create a Subscription

```bash
gcloud pubsub subscriptions create "$SUBSCRIPTION_NAME" \
  --topic="$TOPIC_NAME" \
  --project "$PROJECT_ID"
```

### Step 4: Create a Service Account

```bash
gcloud iam service-accounts create "$SERVICE_ACCOUNT_NAME" \
  --display-name="ServiceHub Dev Service Account" \
  --project "$PROJECT_ID"
```

### Step 5: Grant Roles

```bash
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$SERVICE_ACCOUNT_EMAIL" \
  --role="roles/pubsub.subscriber"
```

If you also need publish capability for testing:

```bash
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$SERVICE_ACCOUNT_EMAIL" \
  --role="roles/pubsub.publisher"
```

### Step 6: Create the JSON Key

```bash
gcloud iam service-accounts keys create ./servicehub-gcp-key.json \
  --iam-account="$SERVICE_ACCOUNT_EMAIL" \
  --project "$PROJECT_ID"
```

---

## How to Test GCP Infrastructure

### Publish a message

```bash
gcloud pubsub topics publish "$TOPIC_NAME" \
  --message='{"orderId":"1001","status":"Created"}' \
  --project "$PROJECT_ID"
```

### Pull the message from the subscription

```bash
gcloud pubsub subscriptions pull "$SUBSCRIPTION_NAME" \
  --limit=1 \
  --auto-ack \
  --project "$PROJECT_ID"
```

If you receive your message, your topic and subscription are working.

### Optional next step

Later, you can add:

- dead-letter topics
- retry policies
- ordering keys
- filtered subscriptions

For beginners, topic plus subscription is the right first milestone.

---

## How to Connect GCP to ServiceHub

1. Open ServiceHub.
2. Go to `Connect`.
3. Select `GCP`.
4. Enter a display name such as `GCP Dev PubSub`.
5. Enter your GCP project ID.
6. Paste the full service account JSON.
7. Choose the environment.
8. Save the connection.

What to expect in the current release:

- the UI supports GCP connection details
- GCP appears in multi-cloud and cloud-bridge related flows
- GCP remains preview regardless of configuration — see [docs/PROVIDER-SUPPORT.md](../../PROVIDER-SUPPORT.md) for exactly what that means and the full capability matrix

Best practice:

- verify `CloudProviders:Gcp:Enabled` is set in your deployed environment before onboarding production projects

---

## GCP Security Advice for Beginners

- Use a dedicated service account for ServiceHub.
- Grant only the minimal Pub/Sub roles needed.
- Store the JSON key securely.
- Rotate and replace service account keys regularly.
- Remove unused keys quickly.

---

## GCP Checklist

- project selected
- Pub/Sub API enabled
- topic created
- subscription created
- service account created
- JSON key created securely
- test message published and pulled
- ServiceHub GCP connection saved