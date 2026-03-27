/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  readonly VITE_APPINSIGHTS_CONNECTION_STRING?: string;
  readonly VITE_APPINSIGHTS_SAMPLING_PERCENTAGE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
