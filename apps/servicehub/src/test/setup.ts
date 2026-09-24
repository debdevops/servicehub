import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'

// waitFor/findBy give up after 1 s by default. Under CPU contention (the .NET suites run beside this one in
// `runtest.sh --all`, and lazy-loaded routes transform on first use) that is shorter than a slow-but-correct
// render, and three tests went red for it. A wrong render still fails; only the patience changes.
configure({ asyncUtilTimeout: 5000 })

// The app reads its version from a define() in vite.config.ts. Tests get a stand-in so a component
// that renders the version does not have to care.
const env = import.meta.env as unknown as Record<string, unknown>
env.VITE_APP_VERSION ??= '4.1.0-test'

// Recent Node versions ship their own `localStorage`, which shadows jsdom's and is unusable without
// a backing file (`window.localStorage` is then undefined). Tests get a plain in-memory store, so
// what a component does with storage is the same on every Node version.
function memoryStorage(): Storage {
  const data = new Map<string, string>()
  return {
    get length() {
      return data.size
    },
    clear: () => data.clear(),
    getItem: (key) => data.get(key) ?? null,
    key: (index) => [...data.keys()][index] ?? null,
    removeItem: (key) => void data.delete(key),
    setItem: (key, value) => void data.set(key, String(value)),
  }
}
Object.defineProperty(window, 'localStorage', { value: memoryStorage(), configurable: true })
