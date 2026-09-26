import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useMe } from '../../hooks/useIdentity'
import { useNamespaces } from '../../hooks/useNamespaces'
import { deadLetterKeys } from '../../hooks/useDeadLetters'
import { toProblem } from '../../lib/api/client'
import { purgeMessage } from '../../lib/api/replay'
import { permission } from '../../lib/permissions'
import { NotAllowed } from '../ui/NotAllowed'

/**
 * Purge one dead letter (unit 6.15), from the drawer. Offered only where the message's cloud can delete one message — elsewhere
 * it says so instead of showing a button that cannot work (R4). A reason is required and kept in the ledger.
 */
export function PurgeAction({ dlqMessageId, namespaceId, active }: { dlqMessageId: number; namespaceId: string; active: boolean }) {
  const ns = useNamespaces().data?.find((n) => n.id === namespaceId)
  const may = permission(useMe().data, 'Operator', 'purge this message', { recover: true, namespaceId })
  const client = useQueryClient()
  const purge = useMutation({ mutationFn: (reason: string) => purgeMessage(dlqMessageId, reason), onSettled: () => void client.invalidateQueries({ queryKey: deadLetterKeys.all }) })
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')

  if (!ns || !active) return null
  if (!ns.capabilities?.supportsPurge) {
    return <p className="text-center text-xs text-[var(--color-text-muted)]">Purge isn’t offered here: this cloud can’t delete one message on its own.</p>
  }
  if (purge.isSuccess) {
    return <p role="status" className="rounded-lg bg-[var(--color-surface-muted)] px-3 py-2 text-sm">{purge.data.message}</p>
  }
  return (
    <div className="text-sm">
      {!open ? (
        <button type="button" onClick={() => setOpen(true)} disabled={!may.allowed} className="mx-auto flex items-center gap-1.5 text-xs font-semibold text-[#b91c1c] hover:underline disabled:opacity-50">
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Purge instead…
        </button>
      ) : (
        <form className="space-y-2 rounded-xl border border-[#fecaca] bg-[#fef2f2] p-3" onSubmit={(e) => { e.preventDefault(); purge.mutate(reason.trim()) }}>
          <p>Deletes it from the dead-letter queue <b>for good</b>. It goes through the same checks as a replay, and the ledger keeps your reason.</p>
          <input value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Why purge it" placeholder="Why? (recorded with your name)" className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-2" />
          <div className="flex gap-2">
            <button type="button" onClick={() => setOpen(false)} className="rounded-lg border border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-1.5 font-semibold">Cancel</button>
            <button type="submit" disabled={!reason.trim() || purge.isPending} className="ml-auto rounded-lg bg-[#dc2626] px-3 py-1.5 font-semibold text-white disabled:opacity-50">{purge.isPending ? 'Purging…' : 'Purge for good'}</button>
          </div>
          {purge.isError && <p role="alert" className="text-[#b91c1c]">{toProblem(purge.error).message}</p>}
        </form>
      )}
      <NotAllowed reason={may.reason} />
    </div>
  )
}
