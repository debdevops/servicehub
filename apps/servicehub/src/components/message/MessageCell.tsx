import type { MessageGist } from '../../lib/api/messageGist'

const shortType = (t: string) => t.replace(/^(application|text)\//, '').replace(/^x-/, '').replace(/\+.*$/, '')
const clip = (s: string, n: number) => (s.length <= n ? s : `${s.slice(0, n)}…`)

function Tag({ label, value, title }: { label?: string; value: string; title?: string }) {
  return (
    <span title={title ?? (label ? `${label}: ${value}` : value)} className="inline-flex max-w-[9rem] items-center gap-1 truncate rounded-md bg-[var(--color-surface-muted)] px-1.5 py-0.5 text-[11px] text-[var(--color-text)]">
      {label && <span className="text-[var(--color-text-muted)]">{label}</span>}
      <span className="truncate font-medium">{value}</span>
    </span>
  )
}

/**
 * One message, recognisable at a glance: the start of its body, a few labels (type, subject, correlation, session, its own
 * properties) and its ID. Everything shown was recorded with the message; a part that was not recorded is simply absent.
 */
export function MessageCell({ messageId, gist, subject, idWidth = 'max-w-[14rem]' }: { messageId: string; gist?: MessageGist | null; subject?: string | null; idWidth?: string }) {
  const props = Object.entries(gist?.properties ?? {})
  const hasTags = !!(gist?.contentType || subject || gist?.correlationId || gist?.sessionId || props.length)
  return (
    <div className="min-w-0 max-w-[24rem]">
      {gist?.preview ? (
        <p title={gist.preview} className="line-clamp-2 [overflow-wrap:anywhere] font-mono text-[11.5px] leading-snug text-[var(--color-text)]">{gist.preview}</p>
      ) : (
        <p className="text-[12px] italic text-[var(--color-text-muted)]">No body recorded</p>
      )}
      {hasTags && (
        <div className="mt-1 flex flex-wrap gap-1">
          {gist?.contentType && <Tag value={shortType(gist.contentType)} title={`Content type: ${gist.contentType}`} />}
          {subject && <Tag label="subject" value={subject} />}
          {gist?.sessionId && <Tag label="session" value={clip(gist.sessionId, 16)} title={`Session ID: ${gist.sessionId}`} />}
          {gist?.correlationId && <Tag label="corr" value={clip(gist.correlationId, 16)} title={`Correlation ID: ${gist.correlationId}`} />}
          {props.map(([k, v]) => <Tag key={k} label={k} value={v} />)}
        </div>
      )}
      <p title={`Message ID: ${messageId}`} className={`mt-1 ${idWidth} truncate font-mono text-[11px] text-[var(--color-text-muted)]`}>{messageId}</p>
    </div>
  )
}
