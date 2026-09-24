import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { fetchLedger, fetchLedgerEntry, fetchRecoverySummary, type LedgerQuery, type RecoveryScope } from '../lib/api/recovery'

export const recoveryKeys = {
  all: ['recovery'] as const,
  summary: (scope: RecoveryScope) => [...recoveryKeys.all, 'summary', scope] as const,
  ledger: (query: LedgerQuery) => [...recoveryKeys.all, 'ledger', query] as const,
  entry: (id: string | null) => [...recoveryKeys.all, 'entry', id] as const,
}

/** The one place "how did recoveries end?" is asked. Simple's percentage and Advanced's breakdown both read it. */
export function useRecoverySummary(scope: RecoveryScope) {
  return useQuery({ queryKey: recoveryKeys.summary(scope), queryFn: () => fetchRecoverySummary(scope), placeholderData: keepPreviousData })
}

export function useLedger(query: LedgerQuery) {
  return useQuery({ queryKey: recoveryKeys.ledger(query), queryFn: () => fetchLedger(query), placeholderData: keepPreviousData })
}

export function useLedgerEntry(id: string | null) {
  return useQuery({ queryKey: recoveryKeys.entry(id), queryFn: () => fetchLedgerEntry(id as string), enabled: id !== null })
}
