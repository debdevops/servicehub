import { useState, useEffect, useCallback } from 'react';
import { Outlet, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { MessageFAB } from '@/components/fab';
import { GuidedTour, isTourCompleted } from '@/components/help/GuidedTour';
import { CommandPalette } from '@/components/CommandPalette';
import { KeyboardShortcutsOverlay } from '@/components/KeyboardShortcutsOverlay';
import { useNamespaces } from '@/hooks/useNamespaces';

export function MainLayout() {
  const [searchParams] = useSearchParams();
  const queryClient = useQueryClient();

  // Guided tour state
  const [tourActive, setTourActive] = useState(false);

  // Command Palette state
  const [paletteOpen, setPaletteOpen] = useState(false);

  // Keyboard shortcuts overlay state
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // Auto-launch tour on first visit
  useEffect(() => {
    if (!isTourCompleted()) {
      // Small delay so DOM is ready for spotlight targeting
      const timer = setTimeout(() => setTourActive(true), 800);
      return () => clearTimeout(timer);
    }
  }, []);

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
  // FAB only visible in non-production environments (Dev/UAT) with Manage permission
  // (required for send, generate, and dead-letter operations)
  const canUseFab = (currentNamespace?.environment === 'Dev' || currentNamespace?.environment === 'Uat') 
    && currentNamespace?.hasManagePermission === true;

  // Determine entity type and names for FAB
  const entityType: 'queue' | 'topic' = topicName ? 'topic' : 'queue';
  const entityName = queueName || topicName || '';

  const handleMessagesGenerated = () => {
    // Invalidate messages query to trigger auto-refresh
    queryClient.invalidateQueries({ queryKey: ['messages'] });
    // Also invalidate queues and topics to update counts in sidebar
    queryClient.invalidateQueries({ queryKey: ['queues'] });
    queryClient.invalidateQueries({ queryKey: ['topics'] });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'] });
  };

  const handleMessageSent = () => {
    // Invalidate messages query to trigger auto-refresh
    queryClient.invalidateQueries({ queryKey: ['messages'] });
    // Also invalidate queues and topics to update counts in sidebar
    queryClient.invalidateQueries({ queryKey: ['queues'] });
    queryClient.invalidateQueries({ queryKey: ['topics'] });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'] });
  };

  return (
    <div className="h-screen flex flex-col bg-gray-50">
      {/* Header */}
      <Header />

      {/* Main Content Area */}
      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <Sidebar />

        {/* Content */}
        <main className="flex-1 overflow-hidden flex flex-col">
          <Outlet />
        </main>
      </div>

      {/* FAB - Only show on messages page, in DEV environment, and only with Manage permission */}
      {isMessagesPage && canUseFab && (
        <MessageFAB 
          namespaceId={namespaceId}
          queueName={entityName}
          entityType={entityType}
          topicName={topicName}
          subscriptionName={subscriptionName}
          environment={currentNamespace?.environment}
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
    </div>
  );
}
