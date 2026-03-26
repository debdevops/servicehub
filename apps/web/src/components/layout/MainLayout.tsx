import { useState, useEffect, useCallback } from 'react';
import { Outlet, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { MessageFAB } from '@/components/fab';
import { GuidedTour, isTourCompleted } from '@/components/help/GuidedTour';
import { useNamespaces } from '@/hooks/useNamespaces';

export function MainLayout() {
  const [searchParams] = useSearchParams();
  const queryClient = useQueryClient();

  // Guided tour state
  const [tourActive, setTourActive] = useState(false);

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
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');
  const topicName = searchParams.get('topic');
  const subscriptionName = searchParams.get('subscription');
  const isMessagesPage = window.location.pathname === '/messages';

  // Resolve current namespace to check environment and permissions
  const { data: namespaces } = useNamespaces();
  const currentNamespace = namespaces?.find(ns => ns.id === namespaceId);
  const isProd = currentNamespace?.environment === 'Prod';
  const canUseFab = !isProd && currentNamespace?.hasSendPermission !== false;

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

      {/* FAB - Only show on messages page, NOT in production, and only with Send permission */}
      {isMessagesPage && canUseFab && (
        <MessageFAB 
          namespaceId={namespaceId}
          queueName={entityName}
          entityType={entityType}
          topicName={topicName}
          subscriptionName={subscriptionName}
          onMessageSent={handleMessageSent}
          onMessagesGenerated={handleMessagesGenerated}
        />
      )}

      {/* Guided Tour Overlay */}
      <GuidedTour isActive={tourActive} onComplete={() => setTourActive(false)} />
    </div>
  );
}
