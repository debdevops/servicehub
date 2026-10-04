import type { StreamEvent } from '../../eventStream'
import type { World } from './model'
import { WORLD_VERSION, buildWorld } from './seed'

const KEY = 'servicehub.demo.world'

let world: World | null = null
let saving: ReturnType<typeof setTimeout> | null = null
const listeners = new Set<(event: StreamEvent) => void>()
let eventCount = 0

function load(): World | null {
  try {
    const raw = window.sessionStorage.getItem(KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as World
    return parsed.version === WORLD_VERSION ? parsed : null
  } catch {
    return null
  }
}

/** The one made-up world. Built from the seed on first use, then kept for the browser session. */
export function getWorld(): World {
  world ??= load() ?? buildWorld()
  return world
}

/** Writes the world to sessionStorage a moment later, so a burst of changes is one write. A full or blocked store only means a refresh starts over. */
export function save(): void {
  if (saving) return
  saving = setTimeout(flush, 250)
}

function flush(): void {
  if (saving) clearTimeout(saving)
  saving = null
  try {
    if (world) window.sessionStorage.setItem(KEY, JSON.stringify(world))
  } catch {
    /* the demo still works in memory */
  }
}

// A change made just before the page is left (a reload, a link to another address) must not be lost to the short delay above.
if (typeof window !== 'undefined') window.addEventListener('pagehide', () => { if (saving) flush() })

/** Throws away everything the visitor did and starts from the seed again. */
export function resetWorld(now: number = Date.now()): World {
  if (saving) clearTimeout(saving)
  saving = null
  try {
    window.sessionStorage.removeItem(KEY)
  } catch {
    /* nothing stored */
  }
  world = buildWorld(now)
  emit('DemoReset', 'System', null)
  return world
}

/** The same events the real server streams, so a screen refreshes itself exactly as it does in the real product. */
export function emit(eventType: string, category: string, namespaceId: string | null): void {
  const ns = namespaceId ? getWorld().namespaces.find((n) => n.id === namespaceId) : undefined
  const event: StreamEvent = { id: `demo-event-${++eventCount}`, eventType, category, occurredUtc: new Date().toISOString(), cloudProvider: ns ? { azure: 'Azure', aws: 'Aws', gcp: 'Gcp' }[ns.provider] : null, namespaceId }
  listeners.forEach((l) => l(event))
}

export const onWorldEvent = (listener: (event: StreamEvent) => void) => {
  listeners.add(listener)
  return () => void listeners.delete(listener)
}
