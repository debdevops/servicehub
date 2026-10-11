/**
 * A small seeded generator (mulberry32). The demo must look the same for every visitor, every test and every screenshot, so
 * nothing in the made-up world may come from Math.random().
 */
export interface Rng {
  /** A number in [0, 1). */
  next(): number
  /** A whole number in [min, max]. */
  int(min: number, max: number): number
  pick<T>(items: readonly T[]): T
  hex(length: number): string
  readonly state: number
}

export function rng(seed: number): Rng {
  let a = seed >>> 0
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
  const int = (min: number, max: number) => min + Math.floor(next() * (max - min + 1))
  return {
    next,
    int,
    pick: (items) => items[int(0, items.length - 1)],
    hex: (length) => Array.from({ length }, () => int(0, 15).toString(16)).join(''),
    get state() {
      return a
    },
  }
}

/** A short, stable hash of some text (FNV-1a) — the demo's stand-in for a signature hash. */
export function hashOf(text: string): string {
  let h1 = 0x811c9dc5
  let h2 = 0x1b873593
  for (let i = 0; i < text.length; i++) {
    h1 = Math.imul(h1 ^ text.charCodeAt(i), 0x01000193) >>> 0
    h2 = Math.imul(h2 ^ text.charCodeAt(i), 0x85ebca6b) >>> 0
  }
  return h1.toString(16).padStart(8, '0') + h2.toString(16).padStart(8, '0')
}
