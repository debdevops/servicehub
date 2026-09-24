import '@testing-library/jest-dom/vitest'

// The app reads its version from a define() in vite.config.ts. Tests get a stand-in so a component
// that renders the version does not have to care.
const env = import.meta.env as unknown as Record<string, unknown>
env.VITE_APP_VERSION ??= '4.1.0-test'
