import '@testing-library/jest-dom';

// Node 22+ defines its own localStorage/sessionStorage globals (undefined unless
// --localstorage-file is passed), which shadow jsdom's implementations because
// vitest skips copying window properties that already exist on globalThis.
// Back-fill the globals with an in-memory Storage implementation.
class MemoryStorage implements Storage {
  private store = new Map<string, string>();
  get length() {
    return this.store.size;
  }
  clear() {
    this.store.clear();
  }
  getItem(key: string) {
    return this.store.get(key) ?? null;
  }
  key(index: number) {
    return [...this.store.keys()][index] ?? null;
  }
  removeItem(key: string) {
    this.store.delete(key);
  }
  setItem(key: string, value: string) {
    this.store.set(key, String(value));
  }
}

for (const key of ['localStorage', 'sessionStorage'] as const) {
  if (!globalThis[key]) {
    Object.defineProperty(globalThis, key, {
      value: new MemoryStorage(),
      configurable: true,
      writable: true,
    });
  }
}

// jsdom defaults to a 1024px viewport, which is narrower than this app's own
// responsive side-panel breakpoints — set a normal desktop width so components
// that read window.innerWidth render their default (non-narrow) state in tests.
Object.defineProperty(window, 'innerWidth', { value: 1440, configurable: true, writable: true });

// jsdom never performs layout, so it doesn't implement ResizeObserver at all. Components that
// measure an element's real size (IconRail's overflow detection) only need the constructor to
// exist and not throw — jsdom's clientHeight is always 0, which those components already treat
// as "not yet measured" rather than "no room."
if (!globalThis.ResizeObserver) {
  class NoopResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  globalThis.ResizeObserver = NoopResizeObserver as unknown as typeof ResizeObserver;
}
