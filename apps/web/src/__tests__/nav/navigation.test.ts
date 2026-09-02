import { describe, it, expect } from 'vitest';
import { NAV_ENTRIES, isNavEntryActive, resolveWorkspaceLabel } from '@/nav/navigation';

function sp(query = ''): URLSearchParams {
  return new URLSearchParams(query);
}

describe('navigation registry (W2.4 — one nav definition)', () => {
  it('has no duplicate ids', () => {
    const ids = NAV_ENTRIES.map((e) => e.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('carries an entry for every Quick Access / Icon Rail destination the old three hand-written lists agreed on', () => {
    const ids = NAV_ENTRIES.map((e) => e.id);
    for (const id of [
      'home', 'dashboard', 'incidents', 'fleet', 'messages-active', 'live-tail',
      'messages-deadletter', 'scheduled', 'cloud-bridge', 'dlq-history', 'rules',
      'approval-queue', 'insights', 'cross-cloud-trace', 'autonomy', 'recovery', 'playbook',
      'governance', 'health', 'audit', 'security', 'advanced-servicehub', 'help',
    ]) {
      expect(ids).toContain(id);
    }
  });

  // F5 regression: IconRail was previously missing Live Tail and CommandPalette was previously
  // missing both Incident Center and Live Tail entirely.
  it('includes Incident Center and Live Tail in both Quick Access and the command palette', () => {
    const incidents = NAV_ENTRIES.find((e) => e.id === 'incidents')!;
    const liveTail = NAV_ENTRIES.find((e) => e.id === 'live-tail')!;
    expect(incidents.quickAccess).toBeDefined();
    expect(incidents.commandPalette).toBeDefined();
    expect(liveTail.quickAccess).toBeDefined();
    expect(liveTail.commandPalette).toBeDefined();
  });

  describe('to()', () => {
    it('applies the demo-mode prefix to an ordinary page', () => {
      const home = NAV_ENTRIES.find((e) => e.id === 'home')!;
      expect(home.to({ navPrefix: '' })).toBe('/home');
      expect(home.to({ navPrefix: '/demo/azure' })).toBe('/demo/azure/home');
    });

    // Connect always leaves Demo Mode — adding a namespace connection is inherently a
    // real-infrastructure action, matching IconRail's settings-gear affordance.
    it('never applies the demo-mode prefix to Connect', () => {
      const connect = NAV_ENTRIES.find((e) => e.id === 'connect')!;
      expect(connect.to({ navPrefix: '/demo/azure' })).toBe('/connect');
    });

    it('includes the current namespace only when one is selected (live-tail, dlq-history)', () => {
      const liveTail = NAV_ENTRIES.find((e) => e.id === 'live-tail')!;
      expect(liveTail.to({ navPrefix: '' })).toBe('/live-tail');
      expect(liveTail.to({ navPrefix: '', currentNamespaceId: 'ns1' })).toBe('/live-tail?namespace=ns1');
    });
  });

  describe('isNavEntryActive', () => {
    const messagesActive = NAV_ENTRIES.find((e) => e.id === 'messages-active')!;
    const messagesDeadletter = NAV_ENTRIES.find((e) => e.id === 'messages-deadletter')!;
    const dashboard = NAV_ENTRIES.find((e) => e.id === 'dashboard')!;

    it('matches a plain basePath entry regardless of query params', () => {
      expect(isNavEntryActive(dashboard, '/dashboard', sp())).toBe(true);
      expect(isNavEntryActive(dashboard, '/fleet', sp())).toBe(false);
    });

    it('strips an optional /demo/{provider} prefix before matching', () => {
      expect(isNavEntryActive(dashboard, '/demo/azure/dashboard', sp())).toBe(true);
    });

    it('differentiates Active Messages from Dead-Letter on the shared messages-overview basePath', () => {
      expect(isNavEntryActive(messagesActive, '/messages-overview', sp('tab=active'))).toBe(true);
      expect(isNavEntryActive(messagesActive, '/messages-overview', sp('tab=deadletter'))).toBe(false);
      expect(isNavEntryActive(messagesDeadletter, '/messages-overview', sp('tab=deadletter'))).toBe(true);
      expect(isNavEntryActive(messagesDeadletter, '/messages-overview', sp('tab=active'))).toBe(false);
    });

    it('does not highlight either Active Messages or Dead-Letter when the URL carries no tab', () => {
      // Both previously matched simultaneously under plain pathname-only matching — the exact
      // bug this predicate exists to fix.
      expect(isNavEntryActive(messagesActive, '/messages-overview', sp())).toBe(false);
      expect(isNavEntryActive(messagesDeadletter, '/messages-overview', sp())).toBe(false);
    });
  });

  describe('resolveWorkspaceLabel', () => {
    // Mirrors QuickAccessToolbar.test.tsx's WORKSPACE_ROUTES table — same source of truth now
    // drives both the toolbar and this registry, so this is a direct regression guard.
    const cases: Array<[string, string, string]> = [
      ['/messages-overview', 'tab=active', 'Active Messages'],
      ['/messages-overview', 'tab=deadletter', 'Dead-Letter'],
      ['/messages', 'queueType=deadletter', 'Dead-Letter'],
      ['/messages', '', 'Active Messages'],
      ['/live-tail', 'namespace=ns1', 'Live Tail'],
      ['/scheduled', 'namespace=ns1', 'Scheduled Messages'],
      ['/dashboard', '', 'Namespace Overview'],
      ['/incidents', '', 'Incident Center'],
      ['/fleet', '', 'Fleet Health'],
      ['/cloud-bridge', '', 'Cloud Bridge'],
      ['/dlq-history', '', 'DLQ Intelligence'],
      ['/signatures', '', 'Failure Signatures'],
      ['/rules', '', 'Auto-Replay Rules'],
      ['/approval-queue', '', 'Approval Queue'],
      ['/autonomy', '', 'Autonomy'],
      ['/insights', '', 'Proactive Insights'],
      ['/cross-cloud-trace', '', 'Multi-Cloud Trace'],
      ['/health', '', 'System Health'],
      ['/audit', '', 'Audit Trail'],
      ['/recovery', '', 'Recovery Evidence'],
      ['/playbook', '', 'Playbook Ledger'],
      ['/governance', '', 'Governance'],
      ['/security', '', 'Security & Privacy'],
      ['/help', '', 'Help & Guide'],
      ['/advanced-servicehub', '', 'Advanced ServiceHub'],
    ];

    it.each(cases)('resolves %s?%s to "%s"', (pathname, query, label) => {
      expect(resolveWorkspaceLabel(pathname, sp(query))).toBe(label);
    });

    it('returns null outside every workspace route', () => {
      expect(resolveWorkspaceLabel('/connect', sp())).toBeNull();
      expect(resolveWorkspaceLabel('/welcome', sp())).toBeNull();
    });

    it('strips an optional /demo/{provider} prefix before resolving', () => {
      expect(resolveWorkspaceLabel('/demo/aws/incidents', sp())).toBe('Incident Center');
    });
  });
});
