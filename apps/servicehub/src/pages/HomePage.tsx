import { TriangleAlert } from 'lucide-react'
import { toProblem } from '../lib/api/client'
import { useSearchParams } from 'react-router-dom'
import { AgentBar } from '../components/agent/AgentBar'
import { NeedsYouStrip } from '../components/pending/NeedsYouStrip'
import { ExplainerCard, ExplainerToggle } from '../components/explainer/Explainer'
import { useExplainer } from '../components/explainer/useExplainer'
import { Welcome } from '../components/connect/Welcome'
import { AllCloudsView } from '../components/home/AllCloudsView'
import { CloudView } from '../components/home/CloudView'
import { ScopeTabs } from '../components/home/ScopeTabs'
import { MessageDrawer } from '../components/message/MessageDrawer'
import { DeadLettersView } from '../components/message/DeadLettersView'
import { ActiveMessagesTab } from '../components/message/ActiveMessagesTab'
import { AutoReplayPage } from '../components/rules/AutoReplayPage'
import { ReplayedTab } from '../components/message/ReplayedTab'
import { NamespaceScope } from '../components/provider/NamespaceScope'
import { resolveScope } from '../components/provider/scopeChoice'
import { useProviderScope } from '../components/provider/providerScope'
import { useNamespaces } from '../hooks/useNamespaces'
import { useHomeScope } from '../lib/home/scope'
import { connectedProviders, providerLabel } from '../lib/providers'

/**
 * Home — the one Simple page (D48, 2026-09-27): everything that used to be split across Home and Fleet
 * Overview. With nothing connected it is the welcome (D45 — there is no Connect page). With one cloud
 * connected there is nothing to choose between, so it is straight to that cloud's detail. With two or
 * more it opens on "All clouds" — every namespace, divided by cloud (§5.2) — and a scope tab switches
 * to one cloud's own detail (§5.3), shaped by what that cloud can actually do (`traitsOf`, never its
 * name — R4).
 */
export function HomePage() {
  const namespaces = useNamespaces()
  const { selected } = useProviderScope()
  const [params, setParams] = useSearchParams()
  // Called unconditionally (rules of hooks) — safe before we know whether anything is connected,
  // since `connectedProviders([])` is just `[]` and every early return below ignores the result.
  const homeScope = useHomeScope(connectedProviders(namespaces.data ?? []).map((p) => p.provider))

  if (namespaces.isError) {
    // The sidebar says it too, but a blank page reads as broken: say it where the person is looking.
    return (
      <section role="alert" className="mx-auto max-w-xl px-6 py-16 text-center">
        <TriangleAlert className="mx-auto mb-3 h-8 w-8 text-[var(--color-warning)]" aria-hidden="true" />
        <h1 className="text-xl font-semibold text-[var(--color-text)]">ServiceHub couldn’t load your clouds</h1>
        <p className="mt-2 text-sm text-[var(--color-text-muted)]">{toProblem(namespaces.error).message}</p>
        <button type="button" onClick={() => void namespaces.refetch()} className="mt-5 rounded-lg bg-[var(--color-primary-600)] px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--color-primary-700)]">
          Try again
        </button>
      </section>
    )
  }
  if (!namespaces.isSuccess) return null
  if (namespaces.data.length === 0) return <Welcome />

  const tab = params.get('tab')
  const connected = connectedProviders(namespaces.data).map((p) => p.provider)
  // Auto Replay opens IN the page (`?panel=rules`, from any link), for the chosen cloud — or the first connected one.
  const rulesProvider = homeScope.provider ?? selected ?? connected[0]
  if (params.get('panel') === 'rules' && rulesProvider) {
    return (
      <>
        <section className="px-6 py-6">
          <AutoReplayPage provider={rulesProvider} onClose={() => setParams((p) => { const n = new URLSearchParams(p); n.delete('panel'); n.delete('rule'); return n }, { replace: true })} />
        </section>
        <MessageDrawer />
      </>
    )
  }
  // A tab (Dead letters / Active / Replayed) is always one cloud's own table: `?provider=` when it is
  // set, otherwise the sidebar's sticky choice (plan §7) — never "All clouds", which has no one table.
  const tabProvider = homeScope.provider ?? selected
  if (tab && tabProvider) {
    const inCloud = namespaces.data.filter((n) => n.provider === tabProvider)
    const choice = resolveScope(inCloud, params)
    const cloud = providerLabel[tabProvider]
    const picker = inCloud.length > 0 ? <div className="px-[22px] pt-4"><NamespaceScope namespaces={inCloud} cloud={cloud} /></div> : null
    return (
      <DrawerAside>
        {tab === 'dlq' && <>{picker}<DeadLettersView provider={tabProvider} namespaces={choice.namespaces} /></>}
        {tab === 'replayed' && <>{picker}<ReplayedTab provider={tabProvider} choice={choice} /></>}
        {tab === 'active' && <>{picker}<ActiveMessagesTab provider={tabProvider} namespaces={choice.namespaces} /></>}
        <MessageDrawer />
      </DrawerAside>
    )
  }

  return <HomeBody connected={connected} allNamespaces={namespaces.data} homeScope={homeScope} />
}

/** Makes room for the message drawer beside the page: it docks at the right, so the page must not sit under it. */
function DrawerAside({ children }: { children: React.ReactNode }) {
  const [params] = useSearchParams()
  const docked = (params.get('message') !== null && params.get('view') !== 'full') || (params.get('tab') === 'active' && params.get('active') !== null)
  return <div className={docked ? 'xl:pr-[462px]' : undefined}>{children}</div>
}

function HomeBody({ connected, allNamespaces, homeScope }: {
  connected: readonly ReturnType<typeof connectedProviders>[number]['provider'][]
  allNamespaces: readonly import('../lib/api/namespaces').Namespace[]
  homeScope: ReturnType<typeof useHomeScope>
}) {
  const [params] = useSearchParams()
  const explainer = useExplainer('home')
  const windowLabel = { '24h': 'Last 24 hours', '7d': 'Last 7 days' } as const

  const provider = homeScope.provider
  const inCloud = provider ? allNamespaces.filter((n) => n.provider === provider) : []
  const choice = provider ? resolveScope(inCloud, params) : null

  return (
    <section className="px-[22px] pb-6 pt-5">
      <header className="mb-[18px] flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--color-text)]">
            Home <ExplainerToggle visible={!explainer.shown} onShow={explainer.show} />
          </h1>
          <p className="mt-[3px] text-[13px] text-[var(--color-text-muted)]">
            What needs you, how every cloud you’ve connected is doing, and the detail for the one you pick.
          </p>
        </div>
        <label className="rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface)] px-[13px] py-1.5 shadow-[var(--shadow-card)]">
          <span className="block text-[9.5px] font-bold uppercase tracking-[0.6px] text-[var(--color-text-muted)]">Window</span>
          <select
            value={homeScope.window}
            onChange={(e) => homeScope.setWindow(e.target.value === '7d' ? '7d' : '24h')}
            className="bg-transparent text-[12.5px] font-semibold text-[#1f2937]"
          >
            <option value="24h">{windowLabel['24h']}</option>
            <option value="7d">{windowLabel['7d']}</option>
          </select>
        </label>
      </header>

      {explainer.shown && <ExplainerCard id="home" onDismiss={explainer.dismiss} />}

      <div className="mb-3.5"><NeedsYouStrip /></div>
      <div className="mb-3.5"><AgentBar namespaces={allNamespaces} queues={null} /></div>

      {connected.length > 1 && (
        <div className="mb-3.5">
          <ScopeTabs connected={connected} selected={provider} onSelect={homeScope.setProvider} />
        </div>
      )}

      {provider && choice ? (
        <CloudView provider={provider} allInCloud={inCloud} choice={choice} window={homeScope.window} />
      ) : (
        <AllCloudsView window={homeScope.window} onOpen={homeScope.setProvider} />
      )}
    </section>
  )
}
