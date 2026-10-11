/**
 * "Open the trace" (4.2.0). Most messages already carry the id of the distributed trace they belong to. ServiceHub reads it
 * from the message's own properties and, if the person has said where their tracing tool lives, links straight to it.
 * Nothing new is stored and nothing is sent: the link is built in the browser and opened by the person.
 */
export interface TraceRef {
  readonly traceId: string
  /** The property it was read from, shown so the person can check it. */
  readonly from: string
}

const HEX32 = /^[0-9a-f]{32}$/i

/** W3C trace context (`traceparent`, and Service Bus's `Diagnostic-Id`, which uses the same form): `00-<trace id>-<span id>-<flags>`. */
function fromW3c(value: string): string | null {
  const parts = value.trim().split('-')
  return parts.length >= 3 && HEX32.test(parts[1]) && !/^0+$/.test(parts[1]) ? parts[1].toLowerCase() : null
}

/** AWS X-Ray's header: `Root=1-5759e988-bd862e3fe1be46a994272793;Parent=…`. The whole root value is the trace id. */
function fromXRay(value: string): string | null {
  return /Root=(1-[0-9a-f]{8}-[0-9a-f]{24})/i.exec(value)?.[1] ?? null
}

const readers: readonly (readonly [key: RegExp, read: (value: string) => string | null])[] = [
  [/^(traceparent|diagnostic-?id)$/i, fromW3c],
  [/^(awstraceheader|x-amzn-trace-id)$/i, fromXRay],
  [/^(trace-?id|x-trace-id|otel-trace-id|googclient_traceid)$/i, (v) => (HEX32.test(v.trim()) ? v.trim().toLowerCase() : null)],
]

/** The trace a message belongs to, read from its application properties (stored JSON text). Null when it carries none. */
export function findTrace(propertiesJson: string | null | undefined): TraceRef | null {
  if (!propertiesJson) return null
  let properties: unknown
  try {
    properties = JSON.parse(propertiesJson)
  } catch {
    return null
  }
  if (!properties || typeof properties !== 'object' || Array.isArray(properties)) return null
  for (const [key, read] of readers) {
    for (const [name, value] of Object.entries(properties as Record<string, unknown>)) {
      if (!key.test(name) || typeof value !== 'string') continue
      const traceId = read(value)
      if (traceId) return { traceId, from: name }
    }
  }
  return null
}

export const TRACE_PLACEHOLDER = '{traceId}'

/** Whether an address can be used as the tracing tool's link: http(s), and it says where the trace id goes. */
export function isTraceTemplate(template: string): boolean {
  if (!template.includes(TRACE_PLACEHOLDER)) return false
  try {
    const url = new URL(template.replace(TRACE_PLACEHOLDER, 'x'))
    return url.protocol === 'https:' || url.protocol === 'http:'
  } catch {
    return false
  }
}

/** The address of one trace in the person's tracing tool, or null if they have not set one up (or it is not usable). */
export function traceLink(template: string, traceId: string): string | null {
  return isTraceTemplate(template) ? template.split(TRACE_PLACEHOLDER).join(encodeURIComponent(traceId)) : null
}
