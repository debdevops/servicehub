import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

export interface BulkGroup {
  readonly reason: string
  readonly selected: number
  readonly willReplay: number
  readonly heldBack: number
}

/** A message the gate would not let through. Always shown with its own reason and remedy — never a bare count. */
export interface BulkHeldBack {
  readonly dlqMessageId: number
  readonly entityName: string
  readonly deadLetterReason: string | null
  readonly reasonCode: string
  readonly remedy: string
}

/** What a bulk replay would do. Stored server-side: the run can only start from this. */
export interface BulkPreview {
  readonly previewId: string
  readonly selected: number
  readonly willReplay: number
  readonly heldBackCount: number
  readonly groups: readonly BulkGroup[]
  readonly heldBack: readonly BulkHeldBack[]
  readonly perSecond: number
  readonly stopAfterConsecutiveFailures: number
  readonly expiresInMinutes: number
  readonly canProveDlqAbsence: boolean
}

/** As the API writes it: enums are camelCase words. */
export type BulkStatus = 'previewed' | 'running' | 'completed' | 'cancelled' | 'stopped' | 'expired'

export interface BulkProgress {
  readonly id: string
  readonly status: BulkStatus
  readonly selected: number
  readonly willReplay: number
  readonly sent: number
  readonly failed: number
  readonly unknown: number
  readonly remaining: number
  readonly heldBack: number
  readonly sampleOnly: boolean
  readonly endedReason: string | null
  readonly previewedAt: string
  readonly startedAt: string | null
  readonly endedAt: string | null
}

export async function previewBulk(dlqMessageIds: readonly number[]): Promise<BulkPreview> {
  return (await api.post<BulkPreview>('/bulk-operations/preview', { dlqMessageIds })).data
}

export async function startBulk(previewId: string, sampleOnly: boolean): Promise<BulkProgress> {
  return (await api.post<BulkProgress>('/bulk-operations', { previewId, sampleOnly }, { headers: withIntent(Intent.BulkReplay) })).data
}

export async function fetchBulk(id: string): Promise<BulkProgress> {
  return (await api.get<BulkProgress>(`/bulk-operations/${id}`)).data
}

export async function cancelBulk(id: string): Promise<BulkProgress> {
  return (await api.post<BulkProgress>(`/bulk-operations/${id}/cancel`)).data
}

/** Whether a job has finished, one way or another. */
export const isEnded = (s: BulkStatus): boolean => s !== 'running' && s !== 'previewed'
