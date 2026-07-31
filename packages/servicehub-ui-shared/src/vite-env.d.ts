/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  readonly VITE_APPINSIGHTS_CONNECTION_STRING?: string;
  readonly VITE_APPINSIGHTS_SAMPLING_PERCENTAGE?: string;
  readonly VITE_ENABLE_AI_INSIGHTS?: string;
  readonly VITE_ENABLE_PERFORMANCE_MONITORING?: string;
  readonly VITE_ENABLE_QUERY_DEVTOOLS?: string;
  readonly VITE_APP_VERSION: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
