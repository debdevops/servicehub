import { api } from './client'
import type { CloudProvider } from './namespaces'

export interface SignatureReplays {
  readonly replayed: number
  readonly stayedFixed: number
  readonly returned: number
  readonly unverified: number
}

export interface Signature {
  readonly signatureHash: string
  readonly provider: CloudProvider
  readonly reason: string
  readonly exampleError: string | null
  readonly entities: readonly string[]
  readonly messages: number
  readonly activeNow: number
  readonly firstSeenAt: string
  readonly lastSeenAt: string
  readonly daily: readonly number[]
  readonly growing: boolean
  readonly replays: SignatureReplays
  /** `helps`, `doesnt`, or `unknown` when nothing was replayed and verified. Never guessed. */
  readonly replayVerdict: 'helps' | 'doesnt' | 'unknown'
}

export interface SignaturePage {
  readonly items: readonly Signature[]
  readonly total: number
  readonly page: number
  readonly pageSize: number
  readonly all: number
  readonly growing: number
  readonly replayHelps: number
  readonly replayDoesNotHelp: number
}

export type SignatureTab = 'all' | 'growing' | 'helps' | 'doesnt'

export interface SignatureQuery {
  readonly provider?: CloudProvider
  readonly days: number
  readonly tab: SignatureTab
  readonly sort: 'messages' | 'recent'
  readonly page: number
}

export async function fetchSignatures(q: SignatureQuery): Promise<SignaturePage> {
  return (await api.get<SignaturePage>('/signatures', { params: { ...q, pageSize: 25 } })).data
}
