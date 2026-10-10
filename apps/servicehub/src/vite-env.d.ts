/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The repository's .version, injected at build time by vite.config.ts. */
  readonly VITE_APP_VERSION: string
  /** 'true' only in the public demo page's build: demo mode is always on and cannot be left. */
  readonly VITE_DEMO_ONLY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
