import type { World } from '../world/model'

/** One request as a demo handler sees it. */
export interface Req {
  readonly w: World
  readonly params: Record<string, unknown>
  readonly body: Record<string, unknown>
  readonly now: number
}

export type Method = 'get' | 'post' | 'put' | 'delete'
export type Handler = (m: RegExpMatchArray, req: Req) => unknown
export type Route = readonly [Method, RegExp, Handler]

/** A refusal in words: the same problem shape the real API answers with, so each screen shows its own error state. */
export class DemoProblem extends Error {
  readonly status: number
  readonly code: string
  constructor(status: number, code: string, detail: string) {
    super(detail)
    this.status = status
    this.code = code
  }
}

export const refuse = (status: number, code: string, detail: string): never => {
  throw new DemoProblem(status, code, detail)
}

export const notFound = (what: string): never => refuse(404, 'not_found', `That ${what} is not in the demo.`)

/** A response that is not plain JSON (a file download). */
export class DemoFile {
  readonly data: Blob
  readonly filename: string
  constructor(data: Blob, filename: string) {
    this.data = data
    this.filename = filename
  }
}

export const DEMO_NOTE = 'Demo — nothing was sent.'

export function page<T>(items: readonly T[], p: Record<string, unknown>, fallbackSize = 25): { items: T[]; page: number; pageSize: number; total: number } {
  const number = Math.max(1, Number(p.page ?? 1))
  const size = Math.max(1, Number(p.pageSize ?? fallbackSize))
  return { items: items.slice((number - 1) * size, number * size), page: number, pageSize: size, total: items.length }
}
