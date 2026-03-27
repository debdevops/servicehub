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
    disableFetchTracking: false,
    enableCorsCorrelation: true,
    enableAutoRouteTracking: true,

    // Reduce telemetry volume
    maxBatchInterval: 15000,           // Batch every 15s instead of default 5s
    maxBatchSizeInBytes: 102400,       // 100KB batch size
    disableExceptionTracking: false,   // Keep exception tracking (critical)
    disableAjaxTracking: false,        // Keep API call tracking

    // Exclude health check and noisy internal endpoints from AJAX tracking
    correlationHeaderExcludedDomains: [],
    excludeRequestFromAutoTrackingPatterns: [
      /\/health/i,
      /\/internal\//i,
    ],
  },
});

// Only initialize if connection string is provided
if (connectionString) {
  appInsights.loadAppInsights();
  appInsights.trackPageView();
}

export { reactPlugin, appInsights };
