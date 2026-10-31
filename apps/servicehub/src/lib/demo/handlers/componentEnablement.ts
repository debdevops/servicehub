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
    appears: 'Appears in the menu as Step Functions. A failed run is listed, and a person with the Approver role can ask AWS to redrive it: AWS restarts the run from the step that failed. The result always stays Unverified.',
    needs: 'To read: states:ListStateMachines, states:ListExecutions and states:DescribeExecution, which start or stop nothing. To let a person redrive (optional, separate): states:RedriveExecution, which Check access does not test.',
    steps: ['Add this policy to the identity ServiceHub signs in as. ListStateMachines cannot be limited to one machine; the other two can.', 'Optional: to let a person redrive a failed run from ServiceHub, add the second policy as well. It was tried with Resource set to *; whether it can be narrowed to one state machine is not yet checked. Without it a redrive is refused and the message says so.', 'Press Check access.'],
    snippets: [
      { label: 'IAM policy', text: '{\n  "Version": "2012-10-17",\n  "Statement": [{\n    "Effect": "Allow",\n    "Action": ["states:ListStateMachines", "states:ListExecutions", "states:DescribeExecution"],\n    "Resource": "*"\n  }]\n}' },
      { label: 'IAM policy to redrive (optional)', text: '{\n  "Version": "2012-10-17",\n  "Statement": [{\n    "Effect": "Allow",\n    "Action": ["states:RedriveExecution"],\n    "Resource": "*"\n  }]\n}' },
    ],
    permissions: ['states:ListStateMachines', 'states:ListExecutions', 'states:DescribeExecution'],
  },
  'logic-apps': {
    what: 'Logic App runs that ended failed, timed out or cancelled, with the error code and message each recorded. Found in every subscription the identity can see.',
    appears: 'Appears in the menu as Logic Apps. A failed run is listed, and a person with the Approver role can ask Azure to resubmit it. A resubmit starts a new run from the trigger and every step runs again, including steps that already succeeded; the original run stays Failed. The result always stays Unverified.',
    needs: 'To read: Reader on the subscription (Microsoft.Logic/workflows/read and Microsoft.Logic/workflows/runs/read), signed in with Entra. To let a person resubmit (optional, separate): Microsoft.Logic/workflows/triggers/histories/resubmit/action, which Reader and Logic App Operator do not include (Logic App Contributor does); a custom role with only that action is the least you can give. Check access does not test it. Reading starts, resubmits or cancels nothing.',
    steps: ['Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential). A Service Bus connection string cannot read Logic Apps.', 'Give that identity the Reader role on the subscription that holds the Logic Apps.', 'Optional: to let a person resubmit a failed run from ServiceHub, also create the one-action role below and give it to the same identity on the same subscription. A resubmit replays the whole workflow, so give it only to an identity you want to be able to do that. Without it a resubmit is refused and the message says so.', 'Press Check access.'],
    snippets: [
      { label: 'Grant the reader role', text: 'az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>' },
      { label: 'Optional: create a role that can only resubmit', text: 'az role definition create --role-definition \'{"Name":"Logic App Run Resubmitter","Description":"Resubmit a Logic Apps run from its trigger history.","Actions":["Microsoft.Logic/workflows/triggers/histories/resubmit/action"],"AssignableScopes":["/subscriptions/<subscription-id>"]}\'' },
      { label: 'Optional: give that role to the identity', text: 'az role assignment create --assignee <the identity> --role "Logic App Run Resubmitter" --scope /subscriptions/<subscription-id>' },
    ],
    permissions: ['Reader', 'Microsoft.Logic/workflows/read', 'Microsoft.Logic/workflows/runs/read'],
  },
  'event-grid': {
    what: 'Events Event Grid could not deliver, kept as blobs in the dead-letter container of an event subscription. Each is listed with the reason and the delivery attempts.',
    appears: 'Appears in the menu as Event Grid. Read only: a dead-lettered event is handed to a person. The event body is not shown. ServiceHub cannot retry these: Event Grid has no way to redeliver an event, and republishing one to the topic would send it to every subscription, including ones that already handled it.',
    needs: 'Reader on the subscription to find event subscriptions, and Storage Blob Data Reader on the storage account that holds the dead-letter container. Signed in with Entra.',
    steps: [
      "Use an Azure connection that signs in with Entra (Managed Identity or Default Azure Credential). No key, secret or connection string is used or stored, and the Event Grid topic access keys in the Azure portal are not needed. A Service Bus connection string cannot read Event Grid. If you have no Azure sign-in connection yet, press Connect at the top of this card: it makes one in a click, with no popup.",
      "Find who that connection signs in as. On a laptop it is the account shown by az account show; in Azure it is the managed identity's object id.",
      'Give that identity the Reader role on the subscription, so it can find event subscriptions. Reader changes nothing.',
      'Give it Storage Blob Data Reader on the one storage account that holds the dead-letter container, not on the subscription. That lets it read blobs and nothing else. It can take a few minutes to apply.',
      'Press Check access.',
    ],
    snippets: [
      { label: '1. Or create the Entra connection by command (what Connect does)', text: `curl -X POST http://localhost:5153/api/v1/namespaces -H 'Content-Type: application/json' -H 'X-ServiceHub-Intent: create-namespace' -d '{"name":"azure-entra","provider":"azure","authType":"defaultAzureCredential","displayName":"Azure sign-in","environment":"dev"}'` },
      { label: '2. Find the identity (a person or app signed in with az login)', text: 'az ad signed-in-user show --query id -o tsv' },
      { label: '3. Grant the reader role', text: 'az role assignment create --assignee <the identity> --role Reader --scope /subscriptions/<subscription-id>' },
      { label: '4. Find the storage account id', text: 'az storage account show --name <storage-account> --query id -o tsv' },
      { label: '5. Grant blob read on that account only', text: 'az role assignment create --assignee <the identity> --role "Storage Blob Data Reader" --scope <storage account resource id>' },
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
    appears: 'Appears in the menu as Eventarc. Read only: an undelivered event is handed to a person. The event body is not shown. To retry one, use Retry in Pub/Sub: its dead-letter topic is a Pub/Sub topic, and Pub/Sub puts the event back on the topic this trigger reads from, so every subscription on that topic receives it again.',
    needs: "Eventarc Viewer (roles/eventarc.viewer) to list triggers, and Pub/Sub Viewer plus Subscriber on the dead-letter topic's subscription to look at what it holds.",
    steps: ['Switch on the Eventarc API in the project.', 'Give the identity ServiceHub signs in as the Eventarc Viewer role.', "Put a dead-letter policy on the trigger's transport subscription, and a pull subscription on the dead-letter topic so something can be looked at.", 'Press Check access.'],
    snippets: [
      { label: 'Enable the API', text: 'gcloud services enable eventarc.googleapis.com --project=<project-id>' },
      { label: 'Grant the viewer role', text: 'gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/eventarc.viewer' },
    ],
    permissions: ['roles/eventarc.viewer', 'roles/pubsub.viewer', 'roles/pubsub.subscriber', 'eventarc.googleapis.com'],
  },
  'cloud-scheduler': {
    what: 'Cloud Scheduler jobs whose last attempt came back with an error, with the status Google recorded. Found across every location.',
    appears: 'Appears in the menu as Cloud Scheduler. Read only: a failed job is detected and handed to a person, who looks at it in the Google Cloud console. Only the last attempt is known, not a history, and a job\'s target is never read.',
    needs: 'Cloud Scheduler Viewer (roles/cloudscheduler.viewer) and the Cloud Scheduler API switched on in the project.',
    steps: ['Switch on the Cloud Scheduler API in the project.', 'Let the identity ServiceHub signs in as read Cloud Scheduler jobs.', 'Press Check access.'],
    snippets: [
      { label: 'Enable the API', text: 'gcloud services enable cloudscheduler.googleapis.com --project=<project-id>' },
      { label: 'Grant the viewer role', text: 'gcloud projects add-iam-policy-binding <project-id> --member=serviceAccount:<the identity> --role=roles/cloudscheduler.viewer' },
    ],
    permissions: ['roles/cloudscheduler.viewer', 'cloudscheduler.googleapis.com'],
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
