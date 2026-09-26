import { api } from './client'
import type { CloudProvider, EnvironmentKind } from './namespaces'

export interface SignatureReplays {
  readonly replayed: number
  readonly stayedFixed: number
  readonly returned: number
  readonly unverified: number
}

/** A namespace the signature was seen in, and how many of its messages are there. */
export interface SignatureNamespace {
  readonly id: string
  readonly name: string
  readonly displayName: string | null
  readonly environment: EnvironmentKind
  readonly messages: number
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
  /** Where it was seen, Production first. */
  readonly namespaces: readonly SignatureNamespace[]
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
  readonly namespaceId?: string
  readonly environment?: EnvironmentKind
  readonly days: number
  readonly tab: SignatureTab
  readonly sort: 'messages' | 'recent'
  readonly page: number
  readonly pageSize: number
}

export async function fetchSignatures(q: SignatureQuery): Promise<SignaturePage> {
  return (await api.get<SignaturePage>('/signatures', { params: q })).data
}

/** What a failure has earned (unit 4.1). Every number is counted from recorded outcomes. */
export interface SignatureTrust {
  /** `approve` — a person approves each replay (the floor) · `standing` / `unattended` — rules may replay it without asking. */
  readonly level: 'approve' | 'standing' | 'unattended'
  readonly sampleSize: number
  /** null when nothing has been verified — never a rate of 0. */
  readonly verifiedSuccessRate: number | null
  readonly recovered: number
  readonly returned: number
  readonly failed: number
  readonly unverified: number
  /** null at the top, in Production, or where the cloud cannot confirm outcomes. */
  readonly nextLevel: 'standing' | 'unattended' | null
  readonly moreVerifiedNeeded: number | null
  readonly rateNeeded: number | null
  readonly cloudCanConfirm: boolean
  readonly productionCeiling: boolean
  readonly reasons: readonly string[]
}

export async function fetchSignatureTrust(hash: string, provider: CloudProvider): Promise<SignatureTrust> {
  return (await api.get<SignatureTrust>(`/signatures/${encodeURIComponent(hash)}/trust`, { params: { provider } })).data
}
