import { focusManager } from '@tanstack/react-query'
import axios, { AxiosError, type AxiosAdapter, type AxiosInstance, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'

/**
 * Forced states (unit 6.1) — DEV ONLY. Makes any API call fail, refuse, hang, go offline or come back
 * empty, so every screen's error / loading / empty / not-allowed state can be looked at on purpose
 * instead of waiting for a bad day. Never included in a production build (main.tsx gates the import).
 *
 * Rules live in localStorage, so they survive a reload and are active before the first request:
 *   /?force=dead-letters:error,recovery:slow      start forcing (reads once, then stays)
 *   /?force=off                                   stop
 *   force.add({ match: 'signatures', mode: 'empty' })   from the console (add `method: 'post'` to hit only writes); force.list() · force.clear()
 *
 * `match` is a substring of the request path (or /regex/). Modes:
 *   error      500 ProblemDetails           forbidden  403 ProblemDetails
 *   network    no response at all           slow       real request, held back `ms` (default 8000)
 *   empty      the real response with every list emptied and every total zeroed — or `body`, when given
 */
export type ForceMode = 'error' | 'forbidden' | 'network' | 'slow' | 'empty'
export interface ForceRule { match: string; mode: ForceMode; ms?: number; body?: unknown; /** Only this HTTP method (e.g. 'post'); omitted = any. */ method?: string }

export const FORCE_KEY = 'servicehub.force'

export function readRules(): ForceRule[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(FORCE_KEY) ?? '[]')
    return Array.isArray(parsed) ? (parsed as ForceRule[]) : []
  } catch { return [] }
}

function writeRules(rules: ForceRule[]) {
  try { window.localStorage.setItem(FORCE_KEY, JSON.stringify(rules)) } catch { /* storage blocked: rules last until reload */ }
  paintBadge(rules)
}

const matches = (rule: ForceRule, url: string, method = 'get') =>
  (!rule.method || rule.method.toLowerCase() === method.toLowerCase()) &&
  (rule.match.startsWith('/') && rule.match.endsWith('/') && rule.match.length > 2
    ? new RegExp(rule.match.slice(1, -1)).test(url)
    : url.includes(rule.match))

/** Every list becomes [] and every count 0, keeping the response's shape — what "nothing here yet" looks like. */
export function blank(value: unknown): unknown {
  if (Array.isArray(value)) return []
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, typeof v === 'number' && /^(total|count|pending|active|dead|waiting)/i.test(k) ? 0 : blank(v)]))
  }
  return value
}

const problem = (status: number, title: string) => ({ type: 'about:blank', title, status, detail: title, code: status === 403 ? 'forbidden' : 'forced_failure' })

function reply(config: InternalAxiosRequestConfig, status: number, data: unknown): Promise<AxiosResponse> {
  const response: AxiosResponse = { data, status, statusText: String(status), headers: {}, config, request: {} }
  return status >= 200 && status < 300
    ? Promise.resolve(response)
    : Promise.reject(new AxiosError(`Forced ${status}`, status >= 500 ? 'ERR_BAD_RESPONSE' : 'ERR_BAD_REQUEST', config, {}, response))
}

/** Wraps whatever adapter the request would have used; requests with no matching rule pass straight through. */
export function forceAdapter(rules: () => ForceRule[], previous?: AxiosAdapter): (config: InternalAxiosRequestConfig) => Promise<AxiosResponse> {
  return async (config) => {
    const real = axios.getAdapter(previous)
    const url = `${config.url ?? ''}`
    const rule = rules().find((r) => matches(r, url, config.method))
    if (!rule) return real(config)
    switch (rule.mode) {
      case 'error': return reply(config, 500, problem(500, 'Forced failure'))
      case 'forbidden': return reply(config, 403, problem(403, 'Forced: not allowed'))
      case 'network': return Promise.reject(new AxiosError('Network Error', 'ERR_NETWORK', config))
      case 'slow': {
        await new Promise((r) => setTimeout(r, rule.ms ?? 8000))
        return real(config)
      }
      case 'empty': {
        if (rule.body !== undefined) return reply(config, 200, rule.body)
        const res = await real(config)
        // A real adapter hands back the raw text; axios parses it only after the adapter returns.
        let data: unknown = res.data
        if (typeof data === 'string') { try { data = JSON.parse(data) } catch { /* not JSON: leave it */ } }
        return { ...res, data: blank(data) }
      }
    }
  }
}

export function installForceStates(api: AxiosInstance) {
  // Read once from the address, then it is just stored rules.
  const asked = new URLSearchParams(window.location.search).get('force')
  if (asked === 'off') writeRules([])
  else if (asked) writeRules(asked.split(',').map((p) => { const [match, mode] = p.split(':'); return { match, mode: (mode || 'error') as ForceMode } }))

  api.interceptors.request.use((config) => {
    if (readRules().some((r) => matches(r, config.url ?? '', config.method))) config.adapter = forceAdapter(readRules, config.adapter as AxiosAdapter | undefined)
    return config
  })

  // A background tab pauses a failed query's retry until it is focused, which would leave a forced error on
  // its loading skeleton forever when driven by automation. Treat the page as focused while forcing is available.
  focusManager.setFocused(true)

  const force = {
    add: (rule: ForceRule) => writeRules([...readRules().filter((r) => r.match !== rule.match), rule]),
    set: (rules: ForceRule[]) => writeRules(rules),
    list: readRules,
    clear: () => writeRules([]),
  }
  Object.assign(window, { force })
  paintBadge(readRules())
}

/** A loud pill, so nobody mistakes a forced screen for a real one. */
function paintBadge(rules: ForceRule[]) {
  if (typeof document === 'undefined') return
  document.getElementById('force-badge')?.remove()
  if (rules.length === 0) return
  const el = document.createElement('div')
  el.id = 'force-badge'
  el.textContent = `FORCING ${rules.map((r) => `${r.match}:${r.mode}`).join(' · ')}`
  el.style.cssText = 'position:fixed;bottom:8px;left:8px;z-index:99999;background:#7c3aed;color:#fff;font:600 11px system-ui;padding:4px 10px;border-radius:999px;pointer-events:none'
  document.body.appendChild(el)
}
