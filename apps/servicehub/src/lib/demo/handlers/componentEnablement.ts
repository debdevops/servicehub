import type { ComponentCheck, ComponentEntry, ComponentList, EnabledComponent } from '../../api/componentEnablement'
import type { CoverageRow } from '../../api/coverage'
import { catalogue } from '../world/catalogue'
import { save } from '../world/store'
import type { World } from '../world/model'
import { notFound, refuse, type Route } from './http'

/**
 * Add a component in the demo. The words mirror `ComponentAccessRules` (backend); the demo's answers come from the demo's own copy of
 * the catalogue. Every connection of a cloud starts with that cloud's read-on-demand component ticked, as the demo's menu always showed.
 */
const SETUPS: Readonly<Record<string, Pick<ComponentEntry, 'what' | 'appears' | 'needs' | 'steps' | 'snippets' | 'permissions'>>> = {
  'step-functions': {
    what: 'State machine runs that ended failed, timed out or aborted, with the error and cause the run recorded.',
    appears: 'Appears in the menu as Step Functions. Read only: a failed run is handed to a person, who re-runs it in the AWS console.',
    needs: 'states:ListStateMachines, states:ListExecutions and states:DescribeExecution. Nothing that starts or stops a run.',
    steps: ['Add this policy to the identity ServiceHub signs in as. ListStateMachines cannot be limited to one machine; the other two can.', 'Press Check access.'],
    snippets: [{ label: 'IAM policy', text: '{\n  "Version": "2012-10-17",\n  "Statement": [{\n    "Effect": "Allow",\n    "Action": ["states:ListStateMachines", "states:ListExecutions", "states:DescribeExecution"],\n    "Resource": "*"\n  }]\n}' }],
    permissions: ['states:ListStateMachines', 'states:ListExecutions', 'states:DescribeExecution'],
  },
  workflows: {
    what: 'Workflow executions that ended failed or cancelled, with the error each recorded. Found across every location.',
    appears: 'Appears in the menu as Workflows. Read only: a failed run is handed to a person, who re-runs it in the Google Cloud console.',
    needs: 'Workflows Viewer (roles/workflows.viewer) and the Workflows APIs switched on in the project.',
    steps: ['Switch on the two Workflows APIs in the project.', 'Let the identity ServiceHub signs in as read workflows.', 'Press Check access.'],
    snippets: [
      { label: 'Enable the APIs', text: 'gcloud services enable workflows.googleapis.com workflowexecutions.googleapis.com --project=<project-id>' },
      { label: 'Grant the viewer role', text: 'gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/workflows.viewer' },
    ],
    permissions: ['roles/workflows.viewer', 'workflows.googleapis.com', 'workflowexecutions.googleapis.com'],
  },
}
const NONE = { what: null, appears: null, needs: null, steps: [], snippets: [], permissions: [] } as const

const camelStatus = (r: CoverageRow) => r.status
const listed = (cloud: string): CoverageRow[] => {
  const seen = new Set<string>()
  return catalogue.filter((r) => r.cloud === cloud && r.status !== 'notSupported' && (r.detection === 'store' || r.detection === 'runs') && !seen.has(r.componentKey) && seen.add(r.componentKey))
}
const toggleable = (r: CoverageRow) => r.detection === 'runs' && r.status !== 'onRequest'
// Ticking is per connection and the menu lists a component when any connection of the cloud has it on. The demo starts with each cloud's
// first connection ticked, so one untick takes the component off the menu and one tick brings it back.
const enabledOf = (w: World): { namespaceId: string; componentKey: string }[] => (w.enabledComponents ??= w.namespaces
  .filter((n, i) => w.namespaces.findIndex((m) => m.provider === n.provider) === i)
  .flatMap((n) => listed(n.provider).filter(toggleable).map((r) => ({ namespaceId: n.id, componentKey: r.componentKey }))))

function entry(w: World, namespaceId: string, r: CoverageRow): ComponentEntry {
  const setup = SETUPS[r.componentKey] ?? NONE
  const alwaysOn = r.detection === 'store' && r.status === 'supported'
  const state: ComponentEntry['state'] = r.status === 'onRequest' ? 'unavailable' : alwaysOn ? 'alwaysOn' : toggleable(r)
    ? (enabledOf(w).some((e) => e.namespaceId === namespaceId && e.componentKey === r.componentKey) ? 'on' : 'off') : 'unavailable'
  return {
    componentKey: r.componentKey, name: r.name, stuckPlace: r.stuckPlace, status: camelStatus(r), statusWords: r.statusWords,
    access: state === 'unavailable' ? 'notBuildable' : 'sameConnection', state, canToggle: toggleable(r), note: r.note, includedIn: null, ...setup,
  }
}

const namespaceOf = (w: World, id: string) => w.namespaces.find((n) => n.id === id) ?? notFound('connection')

export const componentEnablement: readonly Route[] = [
  ['get', /^\/namespaces\/components\/enabled$/, (_m, { w }): EnabledComponent[] =>
    enabledOf(w).flatMap((e) => {
      const ns = w.namespaces.find((n) => n.id === e.namespaceId)
      return ns ? [{ namespaceId: e.namespaceId, cloud: ns.provider, componentKey: e.componentKey }] : []
    })],
  ['get', /^\/namespaces\/([^/]+)\/components$/, (m, { w }): ComponentList => {
    const ns = namespaceOf(w, m[1])
    return { namespaceId: ns.id, connectionName: ns.displayName ?? ns.name, cloud: ns.provider === 'gcp' ? 'Google Cloud' : ns.provider === 'aws' ? 'AWS' : 'Azure',
      components: listed(ns.provider).map((r) => entry(w, ns.id, r)) }
  }],
  ['post', /^\/namespaces\/([^/]+)\/enabled-components\/([^/]+)\/check$/, (m, { w }): ComponentCheck => {
    namespaceOf(w, m[1])
    return { componentKey: m[2], ok: true, reason: null, permissions: SETUPS[m[2]]?.permissions ?? [] }
  }],
  ['put', /^\/namespaces\/([^/]+)\/enabled-components\/([^/]+)$/, (m, { w, body }): ComponentEntry => {
    const ns = namespaceOf(w, m[1])
    const row = listed(ns.provider).find((r) => r.componentKey === m[2]) ?? refuse(404, 'not_found', `'${m[2]}' is not a component of this connection's cloud.`)
    if (!toggleable(row)) refuse(409, 'capability_unavailable', `${row.name} cannot be ticked or unticked here.`)
    const all = enabledOf(w)
    const at = all.findIndex((e) => e.namespaceId === ns.id && e.componentKey === m[2])
    if (body.enabled === true && at < 0) all.push({ namespaceId: ns.id, componentKey: m[2] })
    if (body.enabled !== true && at >= 0) all.splice(at, 1)
    save()
    return entry(w, ns.id, row)
  }],
]
