import type { ComponentCheck, ComponentEntry, ComponentList, EnabledComponent } from '../../api/componentEnablement'
import { readsOnDemand, type CoverageRow } from '../../api/coverage'
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
  'logic-apps': {
    what: 'Logic App runs that ended failed, timed out or cancelled, with the error code and message each recorded. Found in every subscription the identity can see.',
    appears: 'Appears in the menu as Logic Apps. Read only: a failed run is handed to a person, who re-runs it in the Azure portal.',
    needs: 'Reader on the subscription (Microsoft.Logic/workflows/read and Microsoft.Logic/workflows/runs/read), signed in with Entra. Nothing that runs, resubmits or cancels a run.',
    steps: ['Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential). A Service Bus connection string cannot read Logic Apps.', 'Give that identity the Reader role on the subscription that holds the Logic Apps.', 'Press Check access.'],
    snippets: [{ label: 'Grant the reader role', text: 'az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>' }],
    permissions: ['Reader', 'Microsoft.Logic/workflows/read', 'Microsoft.Logic/workflows/runs/read'],
  },
  'event-grid': {
    what: 'Events Event Grid could not deliver, kept as blobs in the dead-letter container of an event subscription. Each is listed with the reason and the delivery attempts.',
    appears: 'Appears in the menu as Event Grid. Read only: a dead-lettered event is handed to a person. The event body is not shown.',
    needs: 'Reader on the subscription to find event subscriptions, and Storage Blob Data Reader on the storage account that holds the dead-letter container. Signed in with Entra.',
    steps: ['Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential).', 'Give that identity the Reader role on the subscription.', 'Give it Storage Blob Data Reader on the storage account that holds the dead-letter container.', 'Press Check access.'],
    snippets: [
      { label: 'Grant the reader role', text: 'az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>' },
      { label: 'Grant blob read', text: 'az role assignment create --assignee <the identity> --role "Storage Blob Data Reader" --scope <storage account resource id>' },
    ],
    permissions: ['Reader', 'Microsoft.EventGrid/eventSubscriptions/read', 'Storage Blob Data Reader'],
  },
  'aws-streams': {
    what: 'Failure records that Lambda writes to an on-failure queue when it cannot process a batch from a Kinesis or DynamoDB stream. Each points at the batch (stream, shard, sequence numbers); the original records are not copied.',
    appears: 'Appears in the menu as Kinesis, DynamoDB streams. Read only: a failure record is handed to a person. The original record expires with the stream and may already be gone.',
    needs: 'lambda:ListEventSourceMappings and sqs:GetQueueAttributes and sqs:ReceiveMessage on the on-failure queue. Nothing that deletes or sends.',
    steps: ['Set an on-failure destination (an SQS queue) on the Lambda event source mapping that reads the stream.', 'Add this policy to the identity ServiceHub signs in as. ReceiveMessage is how a queue is looked at; it deletes nothing, but it counts as a receive, so do not put a redrive policy on the on-failure queue.', 'Press Check access.'],
    snippets: [{ label: 'IAM policy', text: '{\n  "Version": "2012-10-17",\n  "Statement": [\n    { "Effect": "Allow", "Action": ["lambda:ListEventSourceMappings"], "Resource": "*" },\n    { "Effect": "Allow", "Action": ["sqs:GetQueueAttributes", "sqs:ReceiveMessage"], "Resource": "<on-failure queue arn>" }\n  ]\n}' }],
    permissions: ['lambda:ListEventSourceMappings', 'sqs:GetQueueAttributes', 'sqs:ReceiveMessage'],
  },
  eventarc: {
    what: "Events Eventarc could not deliver, kept in the dead-letter topic of the trigger's Pub/Sub subscription. A trigger whose subscription has no dead-letter policy keeps nothing, and the page says so.",
    appears: 'Appears in the menu as Eventarc. Read only: an undelivered event is handed to a person. The event body is not shown.',
    needs: "Eventarc Viewer (roles/eventarc.viewer) to list triggers, and Pub/Sub Viewer plus Subscriber on the dead-letter topic's subscription to look at what it holds.",
    steps: ['Switch on the Eventarc API in the project.', 'Give the identity ServiceHub signs in as the Eventarc Viewer role.', "Put a dead-letter policy on the trigger's transport subscription, and a pull subscription on the dead-letter topic so something can be looked at.", 'Press Check access.'],
    snippets: [
      { label: 'Enable the API', text: 'gcloud services enable eventarc.googleapis.com --project=<project-id>' },
      { label: 'Grant the viewer role', text: 'gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/eventarc.viewer' },
    ],
    permissions: ['roles/eventarc.viewer', 'roles/pubsub.viewer', 'roles/pubsub.subscriber', 'eventarc.googleapis.com'],
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
  return catalogue.filter((r) => r.cloud === cloud && r.status !== 'notSupported' && (r.detection === 'store' || readsOnDemand(r.detection)) && !seen.has(r.componentKey) && seen.add(r.componentKey))
}
const toggleable = (r: CoverageRow) => readsOnDemand(r.detection) && r.status !== 'onRequest'
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
