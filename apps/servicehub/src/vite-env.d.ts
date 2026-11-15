/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The repository's .version, injected at build time by vite.config.ts. */
  readonly VITE_APP_VERSION: string
  /** 'true' only in the public demo page's build: demo mode is always on and cannot be left. */
  readonly VITE_DEMO_ONLY?: string
  /** 'true' only when ./run.sh started ServiceHub Bot and Vite proxies /bot to it (see next/components/BotLauncher.tsx). */
  readonly VITE_SERVICEHUB_BOT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
