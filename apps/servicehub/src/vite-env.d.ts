/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The repository's .version, injected at build time by vite.config.ts. */
  readonly VITE_APP_VERSION: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
