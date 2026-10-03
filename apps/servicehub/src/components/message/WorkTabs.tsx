import { useLocation } from 'react-router-dom'
import { TabBar } from '../ui/TabBar'
import { sectionHelp } from '../../content/sections'

const tabs = [
  { id: 'dlq', label: 'Dead letters' },
  { id: 'active', label: 'Active' },
  { id: 'replayed', label: 'Replayed' },
] as const

/** The three views of Home's table. Each is `?tab=`, so each is linkable and survives a refresh. */
export function WorkTabs({ current, counts = {} }: { current: string; counts?: Partial<Record<(typeof tabs)[number]['id'], number>> }) {
  const { search } = useLocation()
  const hrefFor = (id: string) => {
    const params = new URLSearchParams(search)
    params.set('tab', id)
    ;['page', 'reason', 'entity', 'q', 'message', 'active', 'replay', 'view', 'queue', 'modal'].forEach((k) => params.delete(k))
    return `/?${params.toString()}`
  }
  return (
    <div className="mb-4 overflow-hidden rounded-t-xl">
    <TabBar
      label="Messages"
      active={current}
      onSelect={() => undefined}
      tabs={tabs.map((t) => ({ id: t.id, label: t.label, count: counts[t.id] ?? 'none', to: hrefFor(t.id), help: sectionHelp.tabs[t.id] }))}
    />
    </div>
  )
}
