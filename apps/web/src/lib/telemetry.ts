import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';

const reactPlugin = new ReactPlugin();

// Read connection string from environment variable
const connectionString = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING || '';

const appInsights = new ApplicationInsights({
  config: {
    connectionString,
    extensions: [reactPlugin],

    // Cost-effective settings
    samplingPercentage: Number(import.meta.env.VITE_APPINSIGHTS_SAMPLING_PERCENTAGE) || 50,
    enableCorsCorrelation: true,
    enableAutoRouteTracking: true,

    // ── Privacy & security ────────────────────────────────────────────
    // Never capture request or response bodies — message content must not be sent to telemetry
    disableAjaxTracking: false,        // Track API call durations and status codes (not bodies)
    disableFetchTracking: false,       // Track fetch durations and status codes (not bodies)
    // Do NOT log any user input or identifiers to telemetry
    disableCookiesUsage: true,
    // Do NOT add correlation headers to cross-origin Service Bus requests
    correlationHeaderExcludedDomains: ['*.servicebus.windows.net', '*.azure.com'],
    // Exclude endpoints that carry message body data from AJAX auto-tracking
    excludeRequestFromAutoTrackingPatterns: [
      /\/api\/v1\/namespaces\/[^/]+\/queues\/[^/]+\/messages/i,
      /\/api\/v1\/namespaces\/[^/]+\/topics\//i,
      /\/api\/v1\/correlation/i,
      /\/health/i,
      /\/internal\//i,
    ],
    // ─────────────────────────────────────────────────────────────────

    // Reduce telemetry volume
    maxBatchInterval: 15000,           // Batch every 15s instead of default 5s
    maxBatchSizeInBytes: 102400,       // 100KB batch size
    disableExceptionTracking: false,   // Keep exception tracking (critical for error monitoring)
  },
});

// Only initialize if connection string is provided
if (connectionString) {
  appInsights.loadAppInsights();
  appInsights.trackPageView();
}

export { reactPlugin, appInsights };
