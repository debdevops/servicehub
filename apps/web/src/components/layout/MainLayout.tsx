import { useState, useEffect, useCallback } from 'react';
import { Outlet, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Header } from './Header';
import { IconRail } from './IconRail';
import { QuickAccessPanel } from './QuickAccessPanel';
import { NamespacesPanel } from './NamespacesPanel';
import { Footer } from './Footer';
import { DemoModeBanner } from './DemoModeBanner';
import { MessageFAB } from '@/components/fab';
import { GuidedTour, isTourCompleted } from '@/components/help/GuidedTour';
import { CommandPalette } from '@/components/CommandPalette';
import { KeyboardShortcutsOverlay } from '@/components/KeyboardShortcutsOverlay';
import { useNamespaces } from '@/hooks/useNamespaces';
import { getThemeProvider, setThemeProvider, subscribeThemeProvider } from '@/lib/providerTheme';
import { useDemoContext } from '@/lib/demo/DemoContext';

export function MainLayout() {
  const { isDemoMode } = useDemoContext();
  const [searchParams] = useSearchParams();
  const queryClient = useQueryClient();

  // Guided tour state
  const [tourActive, setTourActive] = useState(false);

  // Command Palette state
  const [paletteOpen, setPaletteOpen] = useState(false);

  // Keyboard shortcuts overlay state
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // Auto-launch tour on first visit — skip in demo mode
  useEffect(() => {
    if (!isDemoMode && !isTourCompleted()) {
      // Small delay so DOM is ready for spotlight targeting
      const timer = setTimeout(() => setTourActive(true), 800);
      return () => clearTimeout(timer);
    }
  }, [isDemoMode]);

  // Listen for "Take a Tour" event from HelpPage
  const handleStartTour = useCallback(() => setTourActive(true), []);
  useEffect(() => {
    window.addEventListener('servicehub:start-tour', handleStartTour);
    return () => window.removeEventListener('servicehub:start-tour', handleStartTour);
  }, [handleStartTour]);

  // Global Cmd+K / Ctrl+K shortcut for Command Palette
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setPaletteOpen(prev => !prev);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // Listen for open-palette event from Header button
  useEffect(() => {
    const handler = () => setPaletteOpen(true);
    window.addEventListener('servicehub:open-palette', handler);
    return () => window.removeEventListener('servicehub:open-palette', handler);
  }, []);

  // Global '?' shortcut — skip when focus is inside a form element
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
      if (e.key === '?') {
        e.preventDefault();
        setShortcutsOpen(prev => !prev);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');
  const topicName = searchParams.get('topic');
  const subscriptionName = searchParams.get('subscription');
  const isMessagesPage = window.location.pathname === '/messages';

  // Resolve current namespace to check environment and permissions
  const { data: namespaces } = useNamespaces();
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);

  // Provider theme: the UI chrome follows the cloud the user is working in
  // (Azure sky blue by default, AWS light orange, GCP light green — see index.css).
  // The sticky signal updates on sidebar namespace clicks and URL navigation.
  const [themeProvider, setThemeProviderState] = useState(getThemeProvider);
  useEffect(() => subscribeThemeProvider(setThemeProviderState), []);
  useEffect(() => {
    setThemeProvider(currentNamespace?.cloudProvider);
  }, [currentNamespace?.cloudProvider]);
  // Quick Access pages keep the neutral light-blue chrome; the provider tint
  // (AWS orange / GCP green) applies only once the user moves into a cloud's
  // message space. The sticky provider signal still updates in the background
  // so the tint is correct the moment a messages view opens.
  const inProviderSpace = window.location.pathname.endsWith('/messages');
  const appliedTheme = inProviderSpace ? themeProvider : 'azure';
  useEffect(() => {
    document.documentElement.dataset.provider = appliedTheme;
    return () => {
      delete document.documentElement.dataset.provider;
    };
  }, [appliedTheme]);
  // FAB: only in DEV environment with Manage (write) permission — never in UAT/Prod or read-only connections
  const canUseFab = currentNamespace?.environment === 'dev' && currentNamespace?.hasManagePermission === true;

  // Determine entity type for FAB
  const entityType: 'queue' | 'topic' = topicName ? 'topic' : 'queue';

  const handleMessagesGenerated = () => {
    // Mark stale without triggering an immediate burst refetch — the next
    // auto-poll interval (7 s) will pick up the fresh counts. This prevents
    // simultaneous requests to every namespace that saturate the rate limit.
    queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['queues'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['topics'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'], refetchType: 'none' });
  };

  const handleMessageSent = () => {
    // Mark stale without immediate burst — useSendMessage onSuccess already
    // issued refetchType:'active' invalidations for the active queries.
    queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['queues'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['topics'], refetchType: 'none' });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'], refetchType: 'none' });
  };

  return (
    <div className="h-screen flex flex-col bg-gray-50">
      {/* Header */}
      <Header />

      {/* Demo Mode Banner — renders only when DemoModeProvider is active */}
      <DemoModeBanner />

      {/* Main Content Area */}
      <div className="flex flex-1 overflow-hidden">
        {/* Slim icon-only nav rail — always visible */}
        <IconRail />

        {/* Quick Access — first panel, collapsible/resizable/draggable */}
        <QuickAccessPanel />

        {/* Namespaces / Connections — second panel, independently resizable */}
        <NamespacesPanel />

        {/* Content */}
        <main className="flex-1 overflow-hidden flex flex-col min-w-0">
          <Outlet />
        </main>
      </div>

      {/* FAB - Show on messages page in DEV with Manage permission, never in Demo mode */}
      {isMessagesPage && canUseFab && !isDemoMode && (
        <MessageFAB
          namespaceId={namespaceId}
          queueName={queueName}
          entityType={entityType}
          topicName={topicName}
          subscriptionName={subscriptionName}
          environment={currentNamespace?.environment}
          cloudProvider={currentNamespace?.cloudProvider}
          onMessageSent={handleMessageSent}
          onMessagesGenerated={handleMessagesGenerated}
        />
      )}

      {/* Guided Tour Overlay */}
      <GuidedTour isActive={tourActive} onComplete={() => setTourActive(false)} />

      {/* Command Palette */}
      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />

      {/* Keyboard Shortcuts Overlay */}
      <KeyboardShortcutsOverlay open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />

      {/* Footer */}
      <Footer />
    </div>
  );
}
