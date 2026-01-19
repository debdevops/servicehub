import { Outlet, useSearchParams } from 'react-router-dom';
import { Header } from './Header';
import { Sidebar } from './Sidebar';
import { MessageFAB } from '@/components/fab';

export function MainLayout() {
  const [searchParams] = useSearchParams();
  const namespaceId = searchParams.get('namespace');
  const queueName = searchParams.get('queue');
  const isMessagesPage = window.location.pathname === '/messages';

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

      {/* FAB - Only show on messages page */}
      {isMessagesPage && (
        <MessageFAB 
          namespaceId={namespaceId}
          queueName={queueName}
          onMessageSent={() => {
            // Trigger refetch via event or context if needed
          }}
          onMessagesGenerated={() => {
            // Trigger refetch via event or context if needed
          }}
        />
      )}
    </div>
  );
}
