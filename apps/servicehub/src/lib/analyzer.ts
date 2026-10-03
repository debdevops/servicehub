/**
 * A plain-English reading of why one message failed, worked out in the browser from what the message
 * itself recorded. It is a SUGGESTION: whoever shows it must badge it as one (R3), because it is a
 * reading of the recorded reason, not a fact the cloud reported.
 *
 * 4.0.0's analyser looked for patterns across many messages; a drawer needs the answer for one, so
 * this is deliberately smaller and nothing is copied from it.
 */
export interface FailureExplanation {
  /** False when there was nothing recorded to read, so the text is guidance rather than a reading — and is not badged as a suggestion. */
  readonly recorded?: boolean
  /** One line for a table row: what went wrong, in plain words. The longer `summary` is for the drawer. */
  readonly headline: string
  readonly summary: string
  /** A field the error names (`customerId`), when it names one. */
  readonly failingField: string | null
  /**
   * Where the text that was read came from when the cloud recorded no reason: the message's own body or its
   * properties. Absent when the reading is of the recorded reason.
   */
  readonly readFrom?: 'body' | 'properties'
}

/** What the message itself carries, for when the cloud recorded nothing. */
export interface MessageEvidence {
  readonly body?: string | null
  /** The application properties as stored JSON text. */
  readonly propertiesJson?: string | null
}

// Keys a producer uses for the error it hit, whatever its naming convention (`errorType`, `error_message`, `Exception`).
const errorKeys = ['errortype', 'exceptiontype', 'errormessage', 'errordescription', 'failurereason', 'exception', 'error']
// A producer that stamps an error key on every message defaults it to one of these when nothing failed.
const noErrorValues = new Set(['none', 'null', 'n/a', 'na', 'unset', 'undefined', 'false', 'ok', ''])

function errorIn(record: unknown): string | null {
  if (!record || typeof record !== 'object' || Array.isArray(record)) return null
  const entries = Object.entries(record as Record<string, unknown>)
  for (const key of errorKeys) {
    for (const [k, v] of entries) {
      // Specific names match anywhere in the key (`shs-error-type`); the bare `error`/`exception` only as the whole key.
      const normal = k.toLowerCase().replace(/[-_\s]/g, '')
      if (key === 'error' || key === 'exception' ? normal !== key : !normal.includes(key)) continue
      if (typeof v === 'string' && !noErrorValues.has(v.trim().toLowerCase())) return v.trim()
      if (v && typeof v === 'object') {
        const nested = (v as Record<string, unknown>).message ?? (v as Record<string, unknown>).type
        if (typeof nested === 'string' && nested.trim()) return nested.trim()
      }
    }
  }
  return null
}

function parse(json: string | null | undefined): unknown {
  if (!json) return null
  try {
    return JSON.parse(json)
  } catch {
    return null
  }
}

// 4.0.0's body patterns: an exception type in a stack-trace line, or a quoted error field in text that is not valid JSON.
const bodyPatterns = [/(?:Exception|Error):\s*([^\n\r]{3,160})/, /"(?:error|exception|errorMessage)"\s*:\s*"([^"]{3,160})"/i]

/**
 * The error a message carries itself, when the cloud recorded none (AWS SQS and Google Pub/Sub dead-letter by
 * policy and say nothing). Properties first — a producer that sets an error attribute meant it — then the body.
 * 4.0.0's analyser did the same; without it those clouds' dead letters had no reading at all.
 */
export function findCarriedError(evidence: MessageEvidence | undefined): { text: string; from: 'body' | 'properties' } | null {
  if (!evidence) return null
  const fromProperties = errorIn(parse(evidence.propertiesJson))
  if (fromProperties) return { text: fromProperties, from: 'properties' }
  const body = evidence.body ?? ''
  const fromJson = errorIn(parse(body))
  if (fromJson) return { text: fromJson, from: 'body' }
  for (const pattern of bodyPatterns) {
    const found = pattern.exec(body)?.[1]?.trim()
    if (found) return { text: found, from: 'body' }
  }
  return null
}

const fieldPatterns = [
  /required (?:field|property)\s+['"`]?([A-Za-z_][\w.]*)['"`]?\s+(?:is\s+)?(?:missing|not (?:set|provided)|null|empty)/i,
  /missing required (?:field|property)[:\s]+['"`]?([A-Za-z_][\w.]*)/i,
  /['"`]?([A-Za-z_][\w.]*)['"`]? (?:is|was) (?:required|missing)/i,
  /(?:invalid|null|empty) (?:value )?(?:for|in) (?:field |property )?['"`]?([A-Za-z_][\w.]*)/i,
]

export function findFailingField(description: string | null): string | null {
  if (!description) return null
  for (const pattern of fieldPatterns) {
    const found = pattern.exec(description)?.[1]
    if (found) return found
  }
  return null
}

export function explainFailure(reason: string | null, description: string | null, deliveryCount: number, evidence?: MessageEvidence): FailureExplanation {
  const failingField = findFailingField(description)
  const text = `${reason ?? ''} ${description ?? ''}`.toLowerCase()
  if (!reason && !description) {
    const carried = findCarriedError(evidence)
    if (carried) {
      const reading = explainFailure(null, carried.text, deliveryCount)
      const where = carried.from === 'body' ? 'body' : 'properties'
      return reading.headline.startsWith('No reading available')
        ? {
            failingField: reading.failingField,
            readFrom: carried.from,
            headline: `The message’s ${where} says: “${carried.text}”`,
            summary: `This cloud records no reason, but the message carries its own error in its ${where}: “${carried.text}”. That is usually what the receiver could not handle.`,
          }
        : { ...reading, readFrom: carried.from }
    }
    // A cloud that dead-letters by policy records nothing. Say what is true and where the answer usually is.
    return {
      failingField,
      headline: 'The cloud recorded no reason — read the message itself',
      summary: 'This cloud set the message aside automatically and does not record why. The message body is the best place to look: whatever the receiver could not handle is usually visible there.',
      recorded: false,
    }
  }
  const times = `${deliveryCount} ${deliveryCount === 1 ? 'time' : 'times'}`

  if (failingField) {
    return {
      failingField,
      headline: `Required field “${failingField}” is missing or wrong`,
      summary: `The message does not have what the receiver needs: “${failingField}” is missing or wrong. Retrying will fail the same way until the message or the receiver changes.`,
    }
  }
  if (/unexpected token|json|parse|deserializ|malformed/.test(text)) {
    return { failingField, headline: 'The message body could not be read — it looks malformed', summary: 'The body could not be read — it looks malformed. Retrying the same body will fail the same way.' }
  }
  if (/schema|validation|invalid/.test(text)) {
    return { failingField, headline: 'The message failed the receiver’s validation', summary: 'The receiver rejected the content of the message. Retrying the same message will be rejected the same way until the sender or the receiver changes.' }
  }
  if (/unauthori[sz]ed|authenticat|forbidden|\b40[13]\b|token expired|access denied/.test(text)) {
    return { failingField, headline: 'The receiver was not allowed to do this', summary: 'A credential or permission was rejected. Fix the credential first — replaying before that will fail again.' }
  }
  if (/ttl|expired|time to live/.test(text) && !/lock/.test(text)) {
    return { failingField, headline: 'Expired before anything processed it', summary: 'The message expired before anything processed it. It is usually safe to replay if the receiver is healthy now.' }
  }
  if (/lock/.test(text)) {
    return { failingField, headline: 'The receiver held it too long and lost its claim', summary: 'The receiver took too long and its claim on the message ran out, so the message went back to the queue. A replay usually works if the receiver is faster now.' }
  }
  if (/timeout|timed out|deadline/.test(text)) {
    return { failingField, headline: 'A downstream call took too long', summary: 'Something the receiver depends on did not answer in time. This usually clears once that dependency is healthy, so a replay often works.' }
  }
  if (/throttl|too many requests|\b429\b|rate limit/.test(text)) {
    return { failingField, headline: 'The receiver was being rate-limited', summary: 'A downstream service was refusing calls because of too much traffic. A replay usually works once traffic settles, and slowly is safest.' }
  }
  if (/not.?found|\b404\b/.test(text)) {
    return { failingField, headline: 'Something the message refers to was not found', summary: 'The message points at something that does not exist (yet). It may work after that thing exists.' }
  }
  if (/maxdeliverycount|max.?delivery|receive count|maxreceive/.test(text)) {
    return {
      failingField,
      headline: `Delivered ${times} and never completed`,
      summary: `It was delivered ${times} and the receiver never finished it, so it was set aside. Whether a replay helps depends on what was failing at the time.`,
    }
  }
  return { failingField, headline: 'No reading available — see the recorded reason', summary: 'ServiceHub has no reading of this one. The reason above is exactly what was recorded.' }
}
