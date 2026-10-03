import type { Message } from './messages'

/**
 * What tells one message from the next in a grid: the start of its body, how it is labelled, and a few of its own properties.
 * All of it was recorded with the message — none of it is ServiceHub's guess.
 */
export interface MessageGist {
  readonly preview: string | null
  readonly contentType: string | null
  readonly correlationId: string | null
  readonly sessionId: string | null
  /** At most three plain-valued application properties. */
  readonly properties: Readonly<Record<string, string>> | null
}

const oneLine = (text: string | null | undefined, max: number): string | null => {
  const t = text?.replace(/\s+/g, ' ').trim()
  return t ? (t.length <= max ? t : `${t.slice(0, max)}…`) : null
}

/** The same shape the API sends for stored messages, built from a peeked one. */
export function gistOfMessage(m: Message): MessageGist {
  const properties: Record<string, string> = {}
  for (const [k, v] of Object.entries(m.applicationProperties ?? {})) {
    if (Object.keys(properties).length === 3) break
    if (/^(correlationId|sessionId|messageId)$/i.test(k) || /^(CloudPubSub|googclient_)/i.test(k)) continue
    const text = typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean' ? oneLine(String(v), 48) : null
    if (text) properties[k] = text
  }
  return {
    preview: oneLine(m.body, 140),
    contentType: m.contentType,
    correlationId: m.correlationId,
    sessionId: m.sessionId,
    properties: Object.keys(properties).length > 0 ? properties : null,
  }
}
