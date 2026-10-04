import { describe, expect, it } from 'vitest'
// @ts-expect-error -- the app has no Node type definitions; vitest runs in Node, from apps/servicehub
import { readdirSync, readFileSync } from 'node:fs'
import { demoRoutes } from '@/lib/demo/adapter'

const API_DIR = 'src/lib/api'

/** Every `api.get('/x')`, `api.post(`/y/${id}`)` … the app makes, read from the source so a new call cannot be forgotten. */
function callsInTheApp(): { method: string; path: string; file: string }[] {
  const calls: { method: string; path: string; file: string }[] = []
  for (const file of (readdirSync(API_DIR) as string[]).filter((f) => f.endsWith('.ts'))) {
    const source = readFileSync(`${API_DIR}/${file}`, 'utf8') as string
    for (const m of source.matchAll(/api\.(get|post|put|delete)(?:<[^(]*>)?\(\s*(['"`])([^'"`]+)\2/g)) {
      // A value in the path (`${id}`) is stood in for by `1`, which is both a number and a name.
      calls.push({ method: m[1], path: m[3].replace(/\$\{[^}]+\}/g, '1'), file })
    }
  }
  return calls
}

describe('the demo answers everything the app asks', () => {
  const calls = callsInTheApp()

  it('finds the app’s API calls', () => {
    expect(calls.length).toBeGreaterThan(60)
  })

  it.each(calls.map((c) => [`${c.method.toUpperCase()} ${c.path}`, c] as const))('%s', (_name, call) => {
    const answered = demoRoutes.some(([method, pattern]) => method === call.method && pattern.test(call.path))
    expect(answered, `${call.file}: the demo has no handler for ${call.method.toUpperCase()} ${call.path} — a screen would say "not in the demo"`).toBe(true)
  })
})
