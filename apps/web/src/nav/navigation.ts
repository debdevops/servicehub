import type { ComponentType } from 'react';
import {
  Home,
  LayoutDashboard,
  AlertTriangle,
  Layers,
  Database,
  Radio,
  Clock,
  Cloud,
  BarChart3,
  Zap,
  CheckCircle2,
  Sparkles,
  Route,
  Gauge,
  ShieldCheck,
  ClipboardList,
  Users,
  Activity,
  Shield,
  GraduationCap,
  HelpCircle,
  MessageSquare,
  Plug,
  FileSearch,
  History,
} from 'lucide-react';

/**
 * The single navigation definition (roadmap W2.4) — routes, labels, icons, command-palette
 * entries, and the workspace toolbar's label lookup, in one place. Before this, IconRail,
 * QuickAccessPanel, CommandPalette and QuickAccessToolbar each hand-declared their own copy of
 * "every page ServiceHub has," and the four had drifted: IconRail was missing Live Tail
 * entirely; CommandPalette was missing Incident Center and Live Tail, used a different icon than
 * the other two surfaces for four separate destinations (Multi-Cloud Trace, System Health,
 * Auto-Replay Rules, DLQ Intelligence/History), and — because it never applied the demo-mode
 * `/demo/{provider}` prefix — silently kicked a user in Demo Mode out of Demo Mode on every
 * jump. This is the fix: one array, consumed by all four.
 */

// Six groups, ordered to match the operator's actual loop — Overview shows what needs attention,
// Observe explains it, Recover acts on it, Autonomous ServiceHub answers what ServiceHub may do on
// its own and proves what it did (Autonomy Control Center → Recovery Evidence → Playbook Ledger →
// Governance), Platform is ServiceHub's own health, accountability and security, Support is help.
// Audit Trail deliberately lives under Platform, not beside the autonomy pillars: it's the
// complete security/accountability record for every action, not a piece of the autonomy model.
// Nothing was removed in this pass, only regrouped: every entry below is still reachable here, in
// the icon rail (for the five busiest), and in the command palette.
export type NavGroup =
  | 'Overview'
  | 'Observe'
  | 'Recover'
  | 'Autonomous ServiceHub'
  | 'Platform'
  | 'Support';

export type NavColor =
  | 'primary'
  | 'indigo'
  | 'red'
  | 'sky'
  | 'emerald'
  | 'blue'
  | 'purple'
  | 'amber'
  | 'violet'
  | 'teal'
  | 'green';

export interface NavLinkContext {
  /** `/demo/{provider}` in Demo Mode, `''` otherwise. Every `to()` below applies it except
   * `connect` — adding a namespace connection is inherently a real-infrastructure action, so it
   * deliberately always leaves Demo Mode, matching IconRail's settings-gear affordance. */
  navPrefix: string;
  /** The namespace currently selected in the URL, if any — only entries that pre-select a
   * namespace in their own link (Live Tail, DLQ Message History) read this. */
  currentNamespaceId?: string;
}

export interface NavEntry {
  /** Stable identity — also the React key everywhere this is rendered. */
  id: string;
  /** The first path segment after an optional `/demo/{provider}` prefix. Drives active-state
   * matching (`isNavEntryActive`) and the workspace toolbar's label lookup
   * (`resolveWorkspaceLabel`) — see `stripDemoPrefix`. */
  basePath: string;
  label: string;
  icon: ComponentType<{ className?: string }>;
  to: (ctx: NavLinkContext) => string;
  /** Narrows "on this entry" beyond a plain basePath match — needed only where basePath alone
   * is ambiguous (messages-overview's Active/Dead-Letter split shares one basePath). */
  isActive?: (searchParams: URLSearchParams) => boolean;
  /** Present only for entries shown in Quick Access panel + Icon Rail. */
  quickAccess?: {
    group: NavGroup;
    color: NavColor;
  };
  /** Present only for entries offered in the command palette. */
  commandPalette?: {
    description: string;
    keywords?: string;
  };
  /** The workspace toolbar's Back/Forward strip label. A function only where the label varies
   * by a query param on a basePath shared with another entry, or read from an in-page tab
   * toggle (messages-overview, messages). The second argument is the Demo Mode route's own
   * provider (null outside Demo Mode) — only Home's label reads it, to say "AWS Home" under
   * `/demo/aws/home` where there's no `?cloud=` param to fall back on. Omit entirely for a page
   * the toolbar should stay silent on (`connect`). */
  toolbarLabel?: string | ((searchParams: URLSearchParams, demoProvider?: string | null) => string);
}

const messagesOverviewToolbarLabel = (searchParams: URLSearchParams): string =>
  searchParams.get('tab') === 'deadletter' ? 'Dead-Letter' : 'Active Messages';

// Home reads `?cloud=azure|aws|gcp` in the real (non-demo) app to show a cloud-specific Home —
// Demo Mode already fixes the provider via the route prefix, so this only matters there.
const HOME_CLOUD_LABELS: Record<string, string> = { azure: 'Azure', aws: 'AWS', gcp: 'GCP' };

const homeToolbarLabel = (searchParams: URLSearchParams, demoProvider?: string | null): string => {
  const cloud = searchParams.get('cloud') ?? demoProvider;
  const cloudLabel = cloud ? HOME_CLOUD_LABELS[cloud] : undefined;
  return cloudLabel ? `${cloudLabel} Home` : 'Home';
};

const messagesToolbarLabel = (searchParams: URLSearchParams): string =>
  searchParams.get('queueType') === 'deadletter' ? 'Dead-Letter' : 'Active Messages';

const withPrefix = (path: string) => (ctx: NavLinkContext): string => `${ctx.navPrefix}${path}`;

const withNamespace = (path: string) => (ctx: NavLinkContext): string =>
  ctx.currentNamespaceId ? `${ctx.navPrefix}${path}?namespace=${ctx.currentNamespaceId}` : `${ctx.navPrefix}${path}`;

export const NAV_ENTRIES: NavEntry[] = [
  {
    id: 'home',
    basePath: 'home',
    label: 'Home',
    icon: Home,
    to: withPrefix('/home'),
    quickAccess: { group: 'Overview', color: 'primary' },
    commandPalette: { description: 'What needs your attention right now', keywords: 'home attention queue' },
    toolbarLabel: homeToolbarLabel,
  },
  {
    id: 'dashboard',
    basePath: 'dashboard',
    label: 'Namespace Overview',
    icon: LayoutDashboard,
    to: withPrefix('/dashboard'),
    quickAccess: { group: 'Overview', color: 'indigo' },
    commandPalette: { description: 'Multi-namespace overview', keywords: 'overview dashboard' },
    toolbarLabel: 'Namespace Overview',
  },
  {
    id: 'incidents',
    basePath: 'incidents',
    label: 'Incident Center',
    icon: AlertTriangle,
    to: withPrefix('/incidents'),
    quickAccess: { group: 'Overview', color: 'red' },
    commandPalette: {
      description: 'Investigate open failure signatures across every namespace',
      keywords: 'incident investigate failure signature center ops',
    },
    toolbarLabel: 'Incident Center',
  },
  {
    id: 'fleet',
    basePath: 'fleet',
    label: 'Fleet Overview',
    icon: Layers,
    to: withPrefix('/fleet'),
    quickAccess: { group: 'Overview', color: 'indigo' },
    commandPalette: { description: 'Dead-letter health across every namespace', keywords: 'fleet operations overnight health' },
    toolbarLabel: 'Fleet Overview',
  },
  {
    id: 'messages-active',
    basePath: 'messages-overview',
    label: 'Active Messages',
    icon: Database,
    to: withPrefix('/messages-overview?tab=active'),
    isActive: (searchParams) => searchParams.get('tab') === 'active',
    quickAccess: { group: 'Observe', color: 'sky' },
    toolbarLabel: messagesOverviewToolbarLabel,
  },
  {
    id: 'live-tail',
    basePath: 'live-tail',
    label: 'Live Tail',
    icon: Radio,
    to: withNamespace('/live-tail'),
    quickAccess: { group: 'Observe', color: 'emerald' },
    commandPalette: {
      description: 'Tail messages in real time as they flow through a queue or topic',
      keywords: 'tail stream realtime live watch',
    },
    toolbarLabel: 'Live Tail',
  },
  {
    id: 'messages-deadletter',
    basePath: 'dlq-overview',
    label: 'Dead-Letter',
    icon: BarChart3,
    to: withPrefix('/dlq-overview'),
    quickAccess: { group: 'Observe', color: 'red' },
    commandPalette: {
      // Forensic pattern/signature investigation lives in Incident Center now — this page owns
      // current DLQ state (counts, trend, top reasons) only, so the description shouldn't imply
      // overlap with it.
      description: 'Current dead-letter state across every connected namespace: counts, trend, and top reasons',
      keywords: 'dlq dead letter overview triage reasons queues topics',
    },
    toolbarLabel: 'Dead-Letter',
  },
  {
    id: 'scheduled',
    basePath: 'scheduled',
    label: 'Scheduled Messages',
    icon: Clock,
    to: withPrefix('/scheduled'),
    quickAccess: { group: 'Observe', color: 'sky' },
    commandPalette: { description: 'View and cancel scheduled deliveries', keywords: 'future timed deliver' },
    toolbarLabel: 'Scheduled Messages',
  },
  {
    id: 'cloud-bridge',
    basePath: 'cloud-bridge',
    label: 'Cloud Bridge',
    icon: Cloud,
    to: withPrefix('/cloud-bridge'),
    quickAccess: { group: 'Observe', color: 'blue' },
    commandPalette: { description: 'Browse queues, topics and subscriptions across clouds', keywords: 'provider status multi cloud' },
    toolbarLabel: 'Cloud Bridge',
  },
  {
    id: 'dlq-history',
    basePath: 'dlq-history',
    label: 'DLQ Message History',
    icon: History,
    to: withNamespace('/dlq-history'),
    quickAccess: { group: 'Observe', color: 'purple' },
    commandPalette: { description: 'Per-namespace dead-letter message audit trail', keywords: 'dead letter poisoned failed history audit' },
    toolbarLabel: 'DLQ Message History',
  },
  {
    id: 'rules',
    basePath: 'rules',
    label: 'Auto-Replay Rules',
    icon: Zap,
    to: withPrefix('/rules'),
    quickAccess: { group: 'Recover', color: 'amber' },
    commandPalette: { description: 'Manage auto-replay configuration', keywords: 'replay retry automation' },
    toolbarLabel: 'Auto-Replay Rules',
  },
  {
    id: 'approval-queue',
    basePath: 'approval-queue',
    label: 'Approval Queue',
    icon: CheckCircle2,
    to: withPrefix('/approval-queue'),
    quickAccess: { group: 'Recover', color: 'amber' },
    commandPalette: { description: 'Rule matches escalated for manual review', keywords: 'approve escalate eligibility gate declined' },
    toolbarLabel: 'Approval Queue',
  },
  {
    id: 'insights',
    basePath: 'insights',
    label: 'Proactive Insights',
    icon: Sparkles,
    to: withPrefix('/insights'),
    quickAccess: { group: 'Observe', color: 'blue' },
    commandPalette: {
      description: 'Auto-narration, correlation findings, backlog forecasts, contract violations',
      keywords: 'narration narrate correlation forecast backlog contract violation drift push proactive',
    },
    toolbarLabel: 'Proactive Insights',
  },
  {
    id: 'cross-cloud-trace',
    basePath: 'cross-cloud-trace',
    label: 'Multi-Cloud Trace',
    icon: Route,
    to: withPrefix('/cross-cloud-trace'),
    quickAccess: { group: 'Observe', color: 'violet' },
    commandPalette: { description: 'Trace messages by correlation ID', keywords: 'trace journey timeline correlation cross cloud' },
    toolbarLabel: 'Multi-Cloud Trace',
  },
  {
    id: 'autonomy',
    basePath: 'autonomy',
    label: 'Autonomy Control Center',
    icon: Gauge,
    to: withPrefix('/autonomy'),
    quickAccess: { group: 'Autonomous ServiceHub', color: 'blue' },
    commandPalette: {
      description: 'What ServiceHub can safely do on its own, why, and what still needs a person',
      keywords: 'autonomy control center trust level standing unattended circuit breaker autonomous ai guardrails safety',
    },
    toolbarLabel: 'Autonomy Control Center',
  },
  {
    id: 'recovery',
    basePath: 'recovery',
    label: 'Recovery Evidence',
    icon: ShieldCheck,
    to: withPrefix('/recovery'),
    quickAccess: { group: 'Autonomous ServiceHub', color: 'teal' },
    commandPalette: {
      description: 'Tamper-evident ledger of every replay and purge ServiceHub has executed',
      keywords: 'recovery ledger evidence replay purge chain',
    },
    toolbarLabel: 'Recovery Evidence',
  },
  {
    id: 'playbook',
    basePath: 'playbook',
    label: 'Playbook Ledger',
    icon: ClipboardList,
    to: withPrefix('/playbook'),
    quickAccess: { group: 'Autonomous ServiceHub', color: 'indigo' },
    commandPalette: {
      description: 'What ServiceHub proposed across all four pillars, and what a human decided',
      keywords: 'playbook proposal prevention rule disposition review',
    },
    toolbarLabel: 'Playbook Ledger',
  },
  {
    id: 'governance',
    basePath: 'governance',
    label: 'Governance',
    icon: Users,
    to: withPrefix('/governance'),
    quickAccess: { group: 'Autonomous ServiceHub', color: 'red' },
    commandPalette: { description: 'Who holds which role, scoped to which namespace and pillar', keywords: 'governance rbac role grant admin operator approver' },
    toolbarLabel: 'Governance',
  },
  {
    id: 'health',
    basePath: 'health',
    label: 'System Health',
    icon: Activity,
    to: withPrefix('/health'),
    quickAccess: { group: 'Platform', color: 'emerald' },
    commandPalette: { description: 'API and service health status', keywords: 'status ping uptime' },
    toolbarLabel: 'System Health',
  },
  {
    id: 'audit',
    basePath: 'audit',
    label: 'Audit Trail',
    icon: Shield,
    to: withPrefix('/audit'),
    quickAccess: { group: 'Platform', color: 'primary' },
    commandPalette: { description: 'Persistent record of critical operations and access events', keywords: 'logs history compliance' },
    toolbarLabel: 'Audit Trail',
  },
  {
    id: 'security',
    basePath: 'security',
    label: 'Security & Privacy',
    icon: Shield,
    to: withPrefix('/security'),
    quickAccess: { group: 'Platform', color: 'green' },
    commandPalette: { description: 'Encryption and data-handling overview', keywords: 'encryption privacy compliance' },
    toolbarLabel: 'Security & Privacy',
  },
  {
    id: 'advanced-servicehub',
    basePath: 'advanced-servicehub',
    label: 'Advanced ServiceHub',
    icon: GraduationCap,
    to: withPrefix('/advanced-servicehub'),
    quickAccess: { group: 'Support', color: 'indigo' },
    commandPalette: {
      description: 'What Advanced ServiceHub means: the autonomy model, evidence, and governance, explained',
      keywords: 'learn advanced servicehub education architecture explain autonomy model ai agent',
    },
    toolbarLabel: 'Advanced ServiceHub',
  },
  {
    id: 'help',
    basePath: 'help',
    label: 'Help & Guide',
    icon: HelpCircle,
    to: withPrefix('/help'),
    quickAccess: { group: 'Support', color: 'primary' },
    commandPalette: { description: 'Quick reference and shortcuts', keywords: 'docs guide keyboard' },
    toolbarLabel: 'Help & Guide',
  },

  // ── Reachable only by drill-down or the command palette — not Quick Access / Icon Rail
  // destinations, but still part of the workspace area the toolbar's Back/Forward covers. ──
  {
    id: 'messages',
    basePath: 'messages',
    label: 'Messages',
    icon: MessageSquare,
    to: withPrefix('/messages'),
    commandPalette: { description: 'Browse and send messages', keywords: 'queue browse send' },
    toolbarLabel: messagesToolbarLabel,
  },
  {
    id: 'signatures',
    basePath: 'signatures',
    label: 'Failure Signatures',
    icon: FileSearch,
    to: withPrefix('/signatures'),
    toolbarLabel: 'Failure Signatures',
  },
  {
    id: 'connect',
    basePath: 'connect',
    label: 'Connect',
    icon: Plug,
    // Deliberately ignores navPrefix — see NavLinkContext's doc comment above.
    to: () => '/connect',
    commandPalette: { description: 'Add or manage cloud namespaces', keywords: 'namespace add connection string' },
    // No toolbarLabel: /connect sits outside the workspace area (QuickAccessToolbar renders
    // nothing there), matching its exclusion from every other Quick Access surface too.
  },
];

/**
 * The Icon Rail's always-visible set (roadmap next-chapter M4.2) — the product decision W2.4
 * deliberately deferred: "how many destinations to show", not "which pages exist". Five, chosen
 * to cover the loop the roadmap itself names — something broke (`home`'s ranked attention queue),
 * look (`incidents`, `dashboard` for a fleet-wide view), approve (`approval-queue`, the one
 * time-sensitive human decision point), and what ServiceHub may do on its own (`autonomy`, the
 * Autonomy Control Center — the front door of Autonomous ServiceHub, which links straight to the
 * Recovery Evidence proof, the Playbook Ledger and Governance). Every other destination is
 * unchanged and still one click away — QuickAccessPanel keeps its full grouped list, and the
 * command palette (`Cmd/Ctrl+K`) already reaches everything. Nothing here removes a capability;
 * it only decides what earns a permanent pixel in a 56px-wide rail.
 */
export const ICON_RAIL_PRIMARY_IDS: readonly string[] = [
  'home',
  'incidents',
  'dashboard',
  'approval-queue',
  'autonomy',
];

/** First path segment after an optional `/demo/{provider}` prefix, plus that provider itself
 * (null outside Demo Mode) — the provider segment is what lets Home's toolbar label say
 * "AWS Home" under `/demo/aws/home`, where there's no `?cloud=` query param to read since the
 * route itself already fixes the provider. */
function stripDemoPrefix(pathname: string): { segments: string[]; demoProvider: string | null } {
  const segments = pathname.split('/').filter(Boolean);
  return segments[0] === 'demo'
    ? { segments: segments.slice(2), demoProvider: segments[1] ?? null }
    : { segments, demoProvider: null };
}

/** Whether `entry` is the one the given location represents — used by IconRail and
 * QuickAccessPanel to decide which link to highlight. */
export function isNavEntryActive(entry: NavEntry, pathname: string, searchParams: URLSearchParams): boolean {
  const { segments } = stripDemoPrefix(pathname);
  if (segments[0] !== entry.basePath) return false;
  return entry.isActive ? entry.isActive(searchParams) : true;
}

/** The workspace toolbar's Back/Forward strip label for the given location, or `null` outside
 * every workspace route (e.g. `/connect`) — replaces the old hand-maintained
 * `WORKSPACE_BASE_PATHS` set + label switch. */
export function resolveWorkspaceLabel(pathname: string, searchParams: URLSearchParams): string | null {
  const { segments, demoProvider } = stripDemoPrefix(pathname);
  const [basePath] = segments;
  if (!basePath) return null;
  const entry = NAV_ENTRIES.find((e) => e.basePath === basePath && e.toolbarLabel !== undefined);
  if (!entry?.toolbarLabel) return null;
  return typeof entry.toolbarLabel === 'function' ? entry.toolbarLabel(searchParams, demoProvider) : entry.toolbarLabel;
}
