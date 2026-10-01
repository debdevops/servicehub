import { Check, TriangleAlert, X } from 'lucide-react'
import { useEffect } from 'react'
import { dismissNotice, useNotice } from '../../lib/notice'

/** The corner notice (see lib/notice): says how an action ended after the panel it was taken in has gone. Fades on its own. */
export function ActionNotice() {
  const notice = useNotice()
  useEffect(() => {
    if (!notice) return
    const t = window.setTimeout(dismissNotice, notice.tone === 'good' ? 8000 : 15000)
    return () => window.clearTimeout(t)
  }, [notice])
  if (!notice) return null
  const tone = notice.tone === 'good' ? 'bg-[#047857]' : notice.tone === 'warn' ? 'bg-[#b45309]' : 'bg-[#b91c1c]'
  return (
    <div role="status" aria-live="polite" className="fixed bottom-5 left-5 z-50 w-[420px] max-w-[calc(100vw-24px)] rounded-2xl bg-[#0f172a] p-4 text-white shadow-2xl">
      <div className="flex items-start gap-3">
        <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${tone}`}>{notice.tone === 'good' ? <Check className="h-4 w-4" aria-hidden="true" /> : <TriangleAlert className="h-4 w-4" aria-hidden="true" />}</span>
        <div className="min-w-0 flex-1">
          <p className="font-bold">{notice.title}</p>
          <p className="mt-0.5 text-sm text-slate-300">{notice.text}</p>
        </div>
        <button type="button" onClick={dismissNotice} aria-label="Dismiss" className="rounded p-1 text-slate-400 hover:text-white"><X className="h-4 w-4" aria-hidden="true" /></button>
      </div>
    </div>
  )
}
