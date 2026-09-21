/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_APPINSIGHTS_CONNECTION_STRING?: string;
  readonly VITE_APPINSIGHTS_SAMPLING_PERCENTAGE?: string;
  readonly VITE_APP_VERSION: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
