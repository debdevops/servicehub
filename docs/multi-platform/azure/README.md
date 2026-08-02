# Azure Infrastructure Setup Guide for ServiceHub

This guide explains how to create Azure Service Bus infrastructure, test it, and connect it to ServiceHub.

It is written for beginners. If this is your first cloud messaging setup, follow the portal steps first and then repeat the same work through Azure CLI.

---

## What You Will Build

This guide helps you create:

- a resource group
- a Service Bus namespace
- a queue
- a topic
- a subscription
- a Shared Access Policy and connection string for ServiceHub

At the end, you will be able to:

- provision Azure infrastructure through the Azure Portal
- provision the same infrastructure through Azure CLI
- send test messages and verify your setup
- connect Azure Service Bus to ServiceHub

---

## Before You Start

Make sure you have:

1. an Azure subscription
2. permission to create Service Bus resources
3. ServiceHub running locally or deployed
4. a non-production environment such as `dev` or `uat`

Recommended approach:

- begin in `dev`
- use small test messages
- use least-privilege credentials
- avoid production until the workflow is proven

---

## How Azure Works in ServiceHub

Azure is the primary, generally available workflow in ServiceHub.

You connect Azure in ServiceHub with:

- a display name
- a Service Bus connection string
- an environment tag such as `dev`, `uat`, or `prod`

After connecting, ServiceHub can help you:

- browse queues and topics
- inspect active and dead-letter messages
- inspect bodies, headers, and properties
- analyze message patterns
- replay or send messages if your credentials allow it

---

## Azure Architecture You Will Create

- Resource Group
- Service Bus Namespace
- Queue
- Topic
- Subscription
- Shared Access Policy or connection string for ServiceHub

---

## Azure Prerequisites

Install Azure CLI if you want the command-line path:

```bash
az --version
```

If needed, install Azure CLI from Microsoft documentation.

Then sign in:

```bash
az login
```

If you have multiple subscriptions, choose one:

```bash
az account list --output table
az account set --subscription "YOUR_SUBSCRIPTION_NAME_OR_ID"
```

---

## Azure Setup Through the Azure Portal UI

### Step 1: Create a Resource Group

1. Open the Azure Portal.
2. Search for `Resource groups`.
3. Click `Create`.
4. Choose your subscription.
5. Enter a resource group name, for example `rg-servicehub-dev`.
6. Choose a region close to your users.
7. Click `Review + create`, then `Create`.

### Step 2: Create a Service Bus Namespace

1. Search for `Service Bus`.
2. Click `Create`.
3. Choose your subscription and resource group.
4. Enter a namespace name, for example `sb-servicehub-dev-001`.
5. Choose a region.
6. Choose a pricing tier.

Recommended for learning:

- `Standard` if you want queues and topics for real testing
- `Premium` only if you need enterprise isolation or higher throughput

7. Click `Review + create`, then `Create`.

### Step 3: Create a Queue

1. Open your Service Bus namespace.
2. In the left menu, click `Queues`.
3. Click `+ Queue`.
4. Enter a name, for example `orders`.
5. Leave defaults if you are learning.
6. Click `Create`.

Useful beginner settings to understand:

- `Max delivery count`
- `Message time to live`
- `Lock duration`

### Step 4: Create a Topic and Subscription

1. In the namespace, click `Topics`.
2. Click `+ Topic`.
3. Name it `events`.
4. Click `Create`.
5. Open the topic.
6. Click `Subscriptions`.
7. Click `+ Subscription`.
8. Name it `events-consumer`.
9. Click `Create`.

### Step 5: Create or Find a Connection String

ServiceHub connects to Azure by connection string.

1. Open your Service Bus namespace.
2. Click `Shared access policies`.
3. Choose an existing policy or create a new one.

Recommended beginner choice:

- create a policy specifically for ServiceHub, such as `servicehub-reader`

Permissions:

- `Listen` is enough for read-only investigation
- `Send` is needed if you want to send or replay messages from ServiceHub
- `Manage` is the strongest option and should be used carefully

4. Open the policy.
5. Copy `Primary Connection String`.

---

## Azure Setup Through Azure CLI

Set variables first:

```bash
RESOURCE_GROUP="rg-servicehub-dev"
LOCATION="eastus"
NAMESPACE="sbservicehubdev001"
QUEUE_NAME="orders"
TOPIC_NAME="events"
SUBSCRIPTION_NAME="events-consumer"
AUTH_RULE="servicehub-reader"
```

### Step 1: Create the Resource Group

```bash
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION"
```

### Step 2: Create the Service Bus Namespace

```bash
az servicebus namespace create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$NAMESPACE" \
  --location "$LOCATION" \
  --sku Standard
```

### Step 3: Create a Queue

```bash
az servicebus queue create \
  --resource-group "$RESOURCE_GROUP" \
  --namespace-name "$NAMESPACE" \
  --name "$QUEUE_NAME"
```

### Step 4: Create a Topic

```bash
az servicebus topic create \
  --resource-group "$RESOURCE_GROUP" \
  --namespace-name "$NAMESPACE" \
  --name "$TOPIC_NAME"
```

### Step 5: Create a Subscription

```bash
az servicebus topic subscription create \
  --resource-group "$RESOURCE_GROUP" \
  --namespace-name "$NAMESPACE" \
  --topic-name "$TOPIC_NAME" \
  --name "$SUBSCRIPTION_NAME"
```

### Step 6: Create a Shared Access Policy

For read-only investigation:

```bash
az servicebus namespace authorization-rule create \
  --resource-group "$RESOURCE_GROUP" \
  --namespace-name "$NAMESPACE" \
  --name "$AUTH_RULE" \
  --rights Listen
```

If you want send or replay capability, use `Listen Send` instead of only `Listen`.

### Step 7: Get the Connection String

```bash
az servicebus namespace authorization-rule keys list \
  --resource-group "$RESOURCE_GROUP" \
  --namespace-name "$NAMESPACE" \
  --name "$AUTH_RULE" \
  --query primaryConnectionString \
  --output tsv
```

---

## How to Test Azure Infrastructure

### Easiest test for beginners

Use the Azure Portal and the built-in Service Bus explorer features if available in your subscription and portal experience.

You can:

- send a small JSON message to `orders`
- publish a message to `events`
- verify the subscription receives it

Example test payload:

```json
{
  "orderId": "1001",
  "customer": "Ada Lovelace",
  "status": "Created"
}
```

### Important note about Azure CLI

Azure CLI is strong for provisioning Service Bus resources, but it is not the easiest tool for day-to-day send, peek, and browse testing.

Beginner-friendly testing choices:

- Azure Portal Service Bus explorer
- ServiceHub itself after connection
- a small SDK script later, if needed

---

## How to Connect Azure to ServiceHub

1. Open ServiceHub.
2. Go to `Connect`.
3. Keep `Azure` selected.
4. Enter a display name such as `Azure Dev Service Bus`.
5. Paste the Service Bus connection string.
6. Choose the environment, usually `DEV` first.
7. Save the connection.

What you can do after connecting Azure:

- browse queues and topics
- inspect message bodies and properties
- investigate dead-letter messages
- use replay and send features if permissions allow it
- use DLQ Intelligence, correlation workflows, and dashboard views

---

## Azure Security Advice for Beginners

- Use a dedicated Shared Access Policy for ServiceHub.
- Start with `Listen` if you only need visibility.
- Use `Send` only if you need replay or test sends.
- Avoid using broad root-level credentials unless necessary.
- Store connection strings securely.

---

## Azure Checklist

- resource group created
- Service Bus namespace created
- queue created
- topic and subscription created
- connection string copied securely
- test message sent
- ServiceHub connection saved