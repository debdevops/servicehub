# Application Insights Setup

This guide explains how to connect Application Insights to your self-hosted ServiceHub instance. Read the first section before proceeding — it covers what data goes where and answers most compliance questions upfront.

---

## Table of contents

1. [Your data, your subscription](#1-your-data-your-subscription)
2. [What Application Insights collects (and what it never touches)](#2-what-application-insights-collects-and-what-it-never-touches)
3. [Data compliance considerations](#3-data-compliance-considerations)
4. [Create an Application Insights resource](#4-create-an-application-insights-resource)
5. [Connect to ServiceHub](#5-connect-to-servicehub)

---

## 1. Your data, your subscription

**This is critical to understand before enabling Application Insights.**

When you self-host ServiceHub and configure Application Insights with **your own connection string**, all telemetry goes to **your Azure Application Insights resource** in **your Azure subscription**. ServiceHub's servers receive nothing. There is no shared telemetry pipeline.

If you leave `ApplicationInsights__ConnectionString` empty — or do not set it at all — **no telemetry is sent anywhere.** Not to Microsoft, not to ServiceHub's hosted instance, not anywhere. ServiceHub functions fully without Application Insights.

This is a completely opt-in feature that only benefits you.

---

## 2. What Application Insights collects (and what it never touches)

### Collected — operational data only

| What | Why it's useful |
|---|---|
| HTTP request counts, status codes, response times | See which API endpoints are slow or failing |
| Dependency call durations | How long calls to Azure Service Bus are taking |
| Application errors and exception types | Catch crashes without ssh'ing into the server |
| Health check results | Know if the app is healthy without manual checks |

### Never collected — guaranteed by the code

| What | Why it's guaranteed |
|---|---|
| Service Bus connection strings | `LogRedactor.cs` strips `SharedAccessKey=...` from every log line and telemetry string before writing |
| Message content | Read transiently from Azure Service Bus to display in the browser — never written to any storage or log |
| API keys or SPA tokens | Stripped from all log output by `LogRedactor.cs` before any telemetry processor runs |
| Your users' business data | ServiceHub is a tool for engineers — it relays what Azure returns to your browser and stores nothing from messages |

**Technical proof**: The code is open source. You can verify these guarantees by reading:
- `services/api/src/ServiceHub.Infrastructure/Security/LogRedactor.cs`
- `services/api/src/ServiceHub.Api/Telemetry/SensitiveDataTelemetryProcessor.cs`
- `services/api/src/ServiceHub.Api/Telemetry/HealthCheckTelemetryFilter.cs`

---

## 3. Data compliance considerations

| Concern | Answer |
|---|---|
| Where does telemetry data go? | Your Azure subscription only. You choose the Azure region when creating the Application Insights resource. |
| GDPR / data residency | Choose an Azure region in your required geography (e.g., `West Europe` for EU data residency). |
| What if I'm in a regulated industry (finance, healthcare)? | Set `ApplicationInsights__ConnectionString` to empty and `VITE_APPINSIGHTS_CONNECTION_STRING` to empty. ServiceHub works fully without any telemetry. Zero data is sent anywhere. |
| Can I see exactly what's sent to Application Insights? | Yes — enable **Live Metrics** in Azure Portal while using ServiceHub to see real-time telemetry in plain text before it's indexed. |
| Does ServiceHub's hosted service get my telemetry if I self-host? | No. When you provide your own connection string, telemetry goes only to your resource. ServiceHub's servers are not in the data path. |

**Recommendation**: For most organisations, Application Insights is safe to enable because data goes only to your own subscription. For highly regulated environments, disable it entirely with an empty connection string — ServiceHub works identically either way.

---

## 4. Create an Application Insights resource

### Step 1 — Create the resource

#### Option A — Azure Portal (recommended)

1. Go to [portal.azure.com](https://portal.azure.com)
2. In the top search bar, search for **Application Insights**
3. Click **+ Create**
4. Fill in:
   - **Resource Group**: `rg-servicehub` *(same as your App Service, to keep resources together)*
   - **Name**: `insights-servicehub`
   - **Region**: Same region as your App Service
   - **Resource Mode**: **Workspace-based** *(recommended — required for newer features)*
5. Click **Review + create** → **Create**
6. Wait for the resource to be created (~30 seconds)

#### Option B — Azure CLI (one command)

```bash
az monitor app-insights component create \
  --app insights-servicehub \
  --location eastus \
  --resource-group rg-servicehub \
  --kind web
```

### Step 2 — Get the connection string

#### Option A — Azure Portal

1. Go to your Application Insights resource (search `insights-servicehub` in Azure Portal)
2. Click **Overview** in the left menu
3. In the top section, find **Connection String**
4. Click the copy icon to copy the full connection string (starts with `InstrumentationKey=...`)

<!-- Screenshot: images/appinsights-connection-string.png — Add this screenshot after deployment -->

#### Option B — Azure CLI (one command)

```bash
az monitor app-insights component show \
  --app insights-servicehub \
  --resource-group rg-servicehub \
  --query connectionString \
  --output tsv
```

Save this connection string — you will use it in the next section.

---

## 5. Connect to ServiceHub

### For Azure App Service deployment

Add in Azure Portal → App Service → **Configuration** → **Application settings**:

| Setting name | Value |
|---|---|
| `ApplicationInsights__ConnectionString` | `InstrumentationKey=...;IngestionEndpoint=...` *(the full string copied above)* |

Or via CLI:

```bash
az webapp config appsettings set \
  --name app-servicehub-yourname \
  --resource-group rg-servicehub \
  --settings ApplicationInsights__ConnectionString="InstrumentationKey=...;IngestionEndpoint=..."
```

Then restart the app for the setting to take effect:

```bash
az webapp restart --name app-servicehub-yourname --resource-group rg-servicehub
```

### For local development

Add to `services/api/src/ServiceHub.Api/appsettings.Local.json`:

```json
{
  "Security": {
    "EncryptionKey": "your-local-key"
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=...",
    "SamplingPercentage": 50
  }
}
```

### Frontend telemetry (optional)

Add to `apps/web/.env.local`:

```
VITE_APPINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=...
VITE_APPINSIGHTS_SAMPLING_PERCENTAGE=33
```

**Sampling percentage guidance:**

| Percentage | When to use |
|---|---|
| `10` | High-traffic production (>1,000 requests/hour). Saves cost significantly. |
| `33` | Standard production. Good balance of coverage and cost. (Default in `appsettings.Production.json`) |
| `50` | Low-traffic production or staging. Good visibility. (Default in `appsettings.json`) |
| `100` | Development and debugging. Capture everything — short-term only; cost rises quickly. |

### Verify data is arriving

After configuration, make a few requests in the ServiceHub UI, then in Azure Portal:

1. Go to your Application Insights resource
2. Click **Transaction search** in the left menu
3. Click **Refresh** — you should see recent requests appear within 1–2 minutes

---

*[← Back to self-hosting index](../README.md)*
