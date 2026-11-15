/**
 * The bot's only door into ServiceHub: GET, to a fixed list of read endpoints, as the person asking.
 *
 * - **GET only.** There is no other method in this file, and `test/guide-only.test.ts` fails if one appears in `src/`.
 * - **Fixed paths.** A path not in `READ_PATHS` is refused before any request is made; nothing a person types becomes a path.
 * - **As the person.** Only their own auth headers are passed on, so ServiceHub shows the bot exactly what it would show
 *   them: no more connections, no other role. The bot holds no credential of its own.
 * - **No bodies.** None of these endpoints is asked for message bodies, and `src/live.ts` never prints a preview.
 */

/** Every endpoint the bot may read, all GET and all already used by the app's own screens. */
export const READ_PATHS = ['/namespaces', '/fleet/components', '/fleet/coverage', '/dead-letters', '/recovery/summary'] as const
export type ReadPath = (typeof READ_PATHS)[number]

export type Query = Readonly<Record<string, string | number | undefined>>

export interface ServiceHubReader {
  get<T>(path: ReadPath, query?: Query): Promise<T>
}

/**
 * The person's own identity, passed on unchanged. `X-ServiceHub-Session` is the browser session id the app sends (not a
 * credential). Anything else (cookies of other sites, `X-MS-CLIENT-PRINCIPAL`, forwarded-for headers) is dropped.
 */
export const FORWARDED_HEADERS = ['authorization', 'cookie', 'x-api-key', 'x-servicehub-session'] as const

/** A read that failed, with a sentence a person can act on. */
export class LiveReadError extends Error {
  readonly status: number | undefined
  constructor(message: string, status?: number) {
    super(message)
    this.name = 'LiveReadError'
    this.status = status
  }
}

export interface HttpReaderOptions {
  /** Where ServiceHub's API answers, e.g. `http://localhost:5153`. `/api/v1` is added here. */
  readonly baseUrl: string
  /** The incoming request's headers; only `FORWARDED_HEADERS` are used. */
  readonly headers?: Readonly<Record<string, string | readonly string[] | undefined>>
  readonly timeoutMs?: number
  /** For tests. */
  readonly fetch?: typeof fetch
}

export const DEFAULT_TIMEOUT_MS = 5_000

export function httpReader(options: HttpReaderOptions): ServiceHubReader {
  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS
  const doFetch = options.fetch ?? fetch
  const headers: Record<string, string> = { accept: 'application/json' }
  for (const name of FORWARDED_HEADERS) {
    const value = options.headers?.[name]
    if (typeof value === 'string' && value) headers[name] = value
  }

  return {
    async get<T>(path: ReadPath, query: Query = {}): Promise<T> {
      if (!(READ_PATHS as readonly string[]).includes(path)) throw new LiveReadError(`The bot does not read ${path}.`)
      const url = new URL(`/api/v1${path}`, options.baseUrl)
      for (const [key, value] of Object.entries(query)) if (value !== undefined) url.searchParams.set(key, String(value))

      let response: Response
      try {
        response = await doFetch(url, { method: 'GET', headers, redirect: 'error', signal: AbortSignal.timeout(timeoutMs) })
      } catch (error) {
        if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
          throw new LiveReadError(`ServiceHub did not answer within ${Math.round(timeoutMs / 1000)} seconds`)
        }
        throw new LiveReadError('ServiceHub could not be reached')
      }

      if (response.status === 401 || response.status === 403) {
        throw new LiveReadError("ServiceHub says you aren't allowed to see this", response.status)
      }
      if (!response.ok) throw new LiveReadError(await problemText(response), response.status)
      try {
        return (await response.json()) as T
      } catch {
        throw new LiveReadError('ServiceHub sent an answer the bot could not read', response.status)
      }
    },
  }
}

/**
 * A short-lived cache of reads, so a conversation does not ask ServiceHub the same thing for every question. Entries are
 * kept per **identity** (a hash of the forwarded auth headers), never shared between people: two people with different
 * roles never see each other's numbers. Failed reads are not kept. Every answer still says when its numbers were read.
 */
export class ReadCache {
  readonly #entries = new Map<string, { at: number; value: Promise<unknown> }>()
  readonly ttlMs: number
  readonly #max: number
  readonly #now: () => number

  constructor(options: { ttlMs?: number; maxEntries?: number; now?: () => number } = {}) {
    this.ttlMs = options.ttlMs ?? 15_000
    this.#max = options.maxEntries ?? 500
    this.#now = options.now ?? Date.now
  }

  /** Wraps a reader for one identity. */
  for(identity: string, inner: ServiceHubReader): ServiceHubReader {
    return {
      get: <T>(path: ReadPath, query: Query = {}): Promise<T> => {
        const key = `${identity}\n${path}?${new URLSearchParams(Object.entries(query).filter((e): e is [string, string | number] => e[1] !== undefined).map(([k, v]) => [k, String(v)])).toString()}`
        const now = this.#now()
        const hit = this.#entries.get(key)
        if (hit && now - hit.at < this.ttlMs) return hit.value as Promise<T>
        const value = inner.get<T>(path, query)
        this.#remember(key, { at: now, value })
        value.catch(() => this.#entries.get(key)?.value === value && this.#entries.delete(key))
        return value
      },
    }
  }

  #remember(key: string, entry: { at: number; value: Promise<unknown> }): void {
    this.#entries.delete(key)
    this.#entries.set(key, entry)
    // Oldest first: a Map keeps insertion order, and every write re-inserts.
    while (this.#entries.size > this.#max) {
      const oldest = this.#entries.keys().next().value
      if (oldest === undefined) break
      this.#entries.delete(oldest)
    }
  }
}

/** A stable, non-reversible key for the person behind a request: their forwarded headers, hashed. */
export async function identityOf(headers: HttpReaderOptions['headers']): Promise<string> {
  const parts = FORWARDED_HEADERS.map((name) => `${name}=${typeof headers?.[name] === 'string' ? headers[name] : ''}`)
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(parts.join('\n')))
  return Buffer.from(digest).toString('hex')
}

/** ServiceHub's errors are ProblemDetails with a sentence for a person in `detail`. */
async function problemText(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: unknown; title?: unknown }
    const text = typeof problem.detail === 'string' ? problem.detail : typeof problem.title === 'string' ? problem.title : ''
    if (text) return `ServiceHub said: ${text.slice(0, 200)}`
  } catch {
    // Not JSON: fall through to the status.
  }
  return `ServiceHub answered ${response.status}`
}
