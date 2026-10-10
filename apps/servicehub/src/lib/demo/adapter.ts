import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { AxiosError } from 'axios'
import { agents } from './handlers/agents'
import { backup } from './handlers/backup'
import { bulk } from './handlers/bulk'
import { deadLetters } from './handlers/deadLetters'
import { fleet } from './handlers/fleet'
import { health } from './handlers/health'
import { DemoFile, DemoProblem, type Method, type Route } from './handlers/http'
import { identity } from './handlers/identity'
import { insights } from './handlers/insights'
import { messages } from './handlers/messages'
import { namespaces } from './handlers/namespaces'
import { pendingWork } from './handlers/pendingWork'
import { recovery } from './handlers/recovery'
import { replays } from './handlers/replay'
import { rules } from './handlers/rules'
import { settings } from './handlers/settings'
import { signatures } from './handlers/signatures'
import { settle } from './world/actions'
import { getWorld } from './world/store'

/**
 * Every endpoint the app calls, answered from the made-up world — one handler file per file in `lib/api/`, so a test can check
 * that nothing the app asks for is left unanswered (`tests/web/unit/lib/demo/coverage.test.ts`).
 */
export const demoRoutes: readonly Route[] = [
  ...health, ...identity, ...namespaces, ...messages, ...deadLetters, ...replays, ...recovery, ...rules, ...signatures, ...pendingWork, ...agents, ...bulk, ...settings, ...insights, ...fleet, ...backup,
]

function problem(config: InternalAxiosRequestConfig, status: number, code: string, detail: string): never {
  const response = { data: { code, detail, title: detail, status }, status, statusText: code, headers: {}, config } as AxiosResponse
  throw new AxiosError(detail, String(status), config, undefined, response)
}

function bodyOf(config: InternalAxiosRequestConfig): Record<string, unknown> {
  if (typeof config.data !== 'string') return (config.data as Record<string, unknown> | null | undefined) ?? {}
  try {
    return (JSON.parse(config.data) as Record<string, unknown> | null) ?? {}
  } catch {
    return {}
  }
}

/**
 * The demo's API: the same client, answering from the made-up world. Nothing is ever sent anywhere. Reads are counted from
 * the world; actions change it, exactly as the real product would record them, and say they were simulated. The few things
 * a demo cannot do (connect a real cloud, back up a database) are refused in words.
 */
export const demoAdapter: AxiosAdapter = async (config) => {
  const url = (config.url ?? '').split('?')[0]
  const method = (config.method ?? 'get').toLowerCase() as Method
  const w = getWorld()
  const now = Date.now()
  // Any watch window that has run out is closed first, so an answer is right even if no timer ever fired.
  settle(w, now)
  for (const [verb, pattern, handler] of demoRoutes) {
    if (verb !== method) continue
    const m = url.match(pattern)
    if (!m) continue
    try {
      const data = handler(m, { w, params: (config.params ?? {}) as Record<string, unknown>, body: bodyOf(config), now })
      if (data instanceof DemoFile) return { data: data.data, status: 200, statusText: 'OK', headers: { 'content-disposition': `attachment; filename="${data.filename}"` }, config }
      return { data, status: data === null ? 204 : 200, statusText: 'OK', headers: {}, config }
    } catch (error) {
      if (error instanceof DemoProblem) problem(config, error.status, error.code, error.message)
      throw error
    }
  }
  // Nothing should ever reach here (the coverage test checks every call the app makes). If something does, it is recorded
  // so the browser tests can fail on it by name.
  const missed = ((globalThis as { __demoUncovered?: string[] }).__demoUncovered ??= [])
  missed.push(`${method.toUpperCase()} ${url}`)
  return problem(config, 404, 'demo_not_covered', 'This part of ServiceHub is not in the demo.')
}
