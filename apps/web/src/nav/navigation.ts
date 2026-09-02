import type { ComponentType } from 'react';
import {
  Home,
  LayoutDashboard,
  AlertTriangle,
  Layers,
  Database,
  Radio,
  AlertCircle,
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

export type NavGroup =
  | 'Overview'
  | 'Browse across clouds'
  | 'Diagnose & automate'
  | 'Advanced ServiceHub'
  | 'Platform'
  | 'Learn ServiceHub'
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
   * namespace in their own link (Live Tail, DLQ Intelligence) read this. */
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
   * toggle (messages-overview, messages). Omit entirely for a page the toolbar should stay
   * silent on (`connect`). */
  toolbarLabel?: string | ((searchParams: URLSearchParams) => string);
}

const messagesOverviewToolbarLabel = (searchParams: URLSearchParams): string =>
  searchParams.get('tab') === 'deadletter' ? 'Dead-Letter' : 'Active Messages';

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
    toolbarLabel: 'Home',
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
    label: 'Fleet Health',
    icon: Layers,
    to: withPrefix('/fleet'),
    quickAccess: { group: 'Overview', color: 'indigo' },
    commandPalette: { description: 'Dead-letter health across every namespace', keywords: 'fleet operations overnight' },
    toolbarLabel: 'Fleet Health',
  },
  {
    id: 'messages-active',
    basePath: 'messages-overview',
    label: 'Active Messages',
    icon: Database,
    to: withPrefix('/messages-overview?tab=active'),
    isActive: (searchParams) => searchParams.get('tab') === 'active',
    quickAccess: { group: 'Browse across clouds', color: 'sky' },
    toolbarLabel: messagesOverviewToolbarLabel,
  },
  {
    id: 'live-tail',
    basePath: 'live-tail',
    label: 'Live Tail',
    icon: Radio,
    to: withNamespace('/live-tail'),
    quickAccess: { group: 'Browse across clouds', color: 'emerald' },
    commandPalette: {
      description: 'Tail messages in real time as they flow through a queue or topic',
      keywords: 'tail stream realtime live watch',
    },
    toolbarLabel: 'Live Tail',
  },
  {
    id: 'messages-deadletter',
    basePath: 'messages-overview',
    label: 'Dead-Letter',
    icon: AlertCircle,
    to: withPrefix('/messages-overview?tab=deadletter'),
    isActive: (searchParams) => searchParams.get('tab') === 'deadletter',
    quickAccess: { group: 'Browse across clouds', color: 'red' },
    toolbarLabel: messagesOverviewToolbarLabel,
  },
  {
    id: 'scheduled',
    basePath: 'scheduled',
    label: 'Scheduled Messages',
    icon: Clock,
    to: withPrefix('/scheduled'),
    quickAccess: { group: 'Browse across clouds', color: 'sky' },
    commandPalette: { description: 'View and cancel scheduled deliveries', keywords: 'future timed deliver' },
    toolbarLabel: 'Scheduled Messages',
  },
  {
    id: 'cloud-bridge',
    basePath: 'cloud-bridge',
    label: 'Cloud Bridge',
    icon: Cloud,
    to: withPrefix('/cloud-bridge'),
    quickAccess: { group: 'Browse across clouds', color: 'blue' },
    commandPalette: { description: 'Browse queues, topics and subscriptions across clouds', keywords: 'provider status multi cloud' },
    toolbarLabel: 'Cloud Bridge',
  },
  {
    id: 'dlq-history',
    basePath: 'dlq-history',
    label: 'DLQ Intelligence',
    icon: BarChart3,
    to: withNamespace('/dlq-history'),
    quickAccess: { group: 'Diagnose & automate', color: 'purple' },
    commandPalette: { description: 'Dead-letter queue audit trail', keywords: 'dead letter poisoned failed' },
    toolbarLabel: 'DLQ Intelligence',
  },
  {
    id: 'rules',
    basePath: 'rules',
    label: 'Auto-Replay Rules',
    icon: Zap,
    to: withPrefix('/rules'),
    quickAccess: { group: 'Diagnose & automate', color: 'amber' },
    commandPalette: { description: 'Manage auto-replay configuration', keywords: 'replay retry automation' },
    toolbarLabel: 'Auto-Replay Rules',
  },
  {
    id: 'approval-queue',
    basePath: 'approval-queue',
    label: 'Approval Queue',
    icon: CheckCircle2,
    to: withPrefix('/approval-queue'),
    quickAccess: { group: 'Diagnose & automate', color: 'amber' },
    commandPalette: { description: 'Rule matches escalated for manual review', keywords: 'approve escalate eligibility gate declined' },
    toolbarLabel: 'Approval Queue',
  },
  {
    id: 'insights',
    basePath: 'insights',
    label: 'Proactive Insights',
    icon: Sparkles,
    to: withPrefix('/insights'),
    quickAccess: { group: 'Diagnose & automate', color: 'blue' },
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
    quickAccess: { group: 'Diagnose & automate', color: 'violet' },
    commandPalette: { description: 'Trace messages by correlation ID', keywords: 'trace journey timeline correlation cross cloud' },
    toolbarLabel: 'Multi-Cloud Trace',
  },
  {
    id: 'autonomy',
    basePath: 'autonomy',
    label: 'Autonomy',
    icon: Gauge,
    to: withPrefix('/autonomy'),
    quickAccess: { group: 'Advanced ServiceHub', color: 'blue' },
    commandPalette: {
      description: 'How autonomous ServiceHub is, per pillar, and what evidence and governance support it',
      keywords: 'autonomy trust level standing unattended circuit breaker autonomous ai governance',
    },
    toolbarLabel: 'Autonomy',
  },
  {
    id: 'recovery',
    basePath: 'recovery',
    label: 'Recovery Evidence',
    icon: ShieldCheck,
    to: withPrefix('/recovery'),
    quickAccess: { group: 'Advanced ServiceHub', color: 'teal' },
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
    quickAccess: { group: 'Advanced ServiceHub', color: 'indigo' },
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
    quickAccess: { group: 'Advanced ServiceHub', color: 'red' },
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
    quickAccess: { group: 'Learn ServiceHub', color: 'indigo' },
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

/** First path segment after an optional `/demo/{provider}` prefix. */
function stripDemoPrefix(pathname: string): string[] {
  const segments = pathname.split('/').filter(Boolean);
  return segments[0] === 'demo' ? segments.slice(2) : segments;
}

/** Whether `entry` is the one the given location represents — used by IconRail and
 * QuickAccessPanel to decide which link to highlight. */
export function isNavEntryActive(entry: NavEntry, pathname: string, searchParams: URLSearchParams): boolean {
  const [basePath] = stripDemoPrefix(pathname);
  if (basePath !== entry.basePath) return false;
  return entry.isActive ? entry.isActive(searchParams) : true;
}

/** The workspace toolbar's Back/Forward strip label for the given location, or `null` outside
 * every workspace route (e.g. `/connect`) — replaces the old hand-maintained
 * `WORKSPACE_BASE_PATHS` set + label switch. */
export function resolveWorkspaceLabel(pathname: string, searchParams: URLSearchParams): string | null {
  const [basePath] = stripDemoPrefix(pathname);
  if (!basePath) return null;
  const entry = NAV_ENTRIES.find((e) => e.basePath === basePath && e.toolbarLabel !== undefined);
  if (!entry?.toolbarLabel) return null;
  return typeof entry.toolbarLabel === 'function' ? entry.toolbarLabel(searchParams) : entry.toolbarLabel;
}
