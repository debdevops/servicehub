import { useState, useRef, useEffect } from 'react';
import { Send, Wand2, Plus, X, Skull, RefreshCw } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { SendMessageModal, type MessagePayload } from './SendMessageModal';
import { MessageGeneratorModal } from './MessageGeneratorModal';
import { messagesApi } from '@/lib/api/messages';
import toast from 'react-hot-toast';

// ============================================================================
// MessageFAB - Enhanced Floating Action Button with multiple actions
// Master control for all Service Bus testing operations
// Handles both Queues and Topic Subscriptions
// ============================================================================

interface MessageFABProps {
  namespaceId?: string | null;
  queueName?: string | null;
  entityType?: 'queue' | 'topic';
  topicName?: string | null;
  subscriptionName?: string | null;
  onMessageSent?: (payload: MessagePayload) => void;
  onMessagesGenerated?: () => void;
}

type ModalType = 'send' | 'generate' | null;

export function MessageFAB({ 
  namespaceId, 
  queueName, 
  entityType = 'queue',
  topicName,
  subscriptionName,
  onMessageSent,
  onMessagesGenerated,
}: MessageFABProps) {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [activeModal, setActiveModal] = useState<ModalType>(null);
  const [isDeadLettering, setIsDeadLettering] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();

  // Determine if we have a valid entity selected
  const hasValidEntity = namespaceId && (queueName || (topicName && subscriptionName));

  // Close menu when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsMenuOpen(false);
      }
    };

    if (isMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside);
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [isMenuOpen]);

  // Refresh all queries to get latest data
  const refreshAll = () => {
    queryClient.invalidateQueries({ queryKey: ['messages'] });
    queryClient.invalidateQueries({ queryKey: ['queues'] });
    queryClient.invalidateQueries({ queryKey: ['topics'] });
    queryClient.invalidateQueries({ queryKey: ['subscriptions'] });
    toast.success('Data refreshed');
  };

  const handleSend = (payload: MessagePayload) => {
    const count = payload.messageCount;
    const entityDisplay = `${payload.entityType === 'topic' ? '📢' : '📥'} ${payload.entity}`;
    
    toast.success(
      count > 1 
        ? `Sent ${count} messages to ${entityDisplay}`
        : `Message sent to ${entityDisplay}`
    );

    onMessageSent?.(payload);
    setActiveModal(null);
    
    // Cache invalidation now happens in useSendMessage hook - no delay needed
  };

  const handleOpenSend = () => {
    setIsMenuOpen(false);
    setActiveModal('send');
  };

  const handleOpenGenerate = () => {
    setIsMenuOpen(false);
    setActiveModal('generate');
  };

  const handleDeadLetter = async () => {
    if (!namespaceId) {
      toast.error('Please select a namespace first');
      return;
    }

    // For topics, we need both topicName and subscriptionName
    if (entityType === 'topic') {
      if (!topicName || !subscriptionName) {
        toast.error('Please select a subscription to dead-letter messages');
        return;
      }
    } else {
      if (!queueName) {
        toast.error('Please select a queue first');
        return;
      }
    }

    setIsDeadLettering(true);
    setIsMenuOpen(false);

    try {
      let result;
      
      if (entityType === 'topic' && topicName && subscriptionName) {
        // Dead-letter from topic subscription
        result = await messagesApi.deadLetter(
          namespaceId,
          topicName,
          3, // Dead-letter 3 messages for testing
          'TestingDLQ',
          'Manually moved to DLQ for testing purposes via ServiceHub UI',
          'topic',
          subscriptionName
        );
        toast.success(`✅ Moved ${result.deadLetteredCount} messages to DLQ from ${topicName}/${subscriptionName}`);
      } else if (queueName) {
        // Dead-letter from queue
        result = await messagesApi.deadLetter(
          namespaceId,
          queueName,
          3, // Dead-letter 3 messages for testing
          'TestingDLQ',
          'Manually moved to DLQ for testing purposes via ServiceHub UI',
          'queue'
        );
        toast.success(`✅ Moved ${result.deadLetteredCount} messages to DLQ from ${queueName}`);
      }

      if (result && result.deadLetteredCount > 0) {
        // Refresh to show updated counts immediately with refetch
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'active' }),
          queryClient.invalidateQueries({ queryKey: ['queues', namespaceId], refetchType: 'active' }),
          queryClient.invalidateQueries({ queryKey: ['topics', namespaceId], refetchType: 'active' }),
          queryClient.invalidateQueries({ queryKey: ['subscriptions', namespaceId], refetchType: 'active' }),
        ]);
      } else if (result && result.deadLetteredCount === 0) {
        toast('No messages available to dead-letter', { icon: 'ℹ️' });
      }
    } catch (error: any) {
      const errorDetail = error?.response?.data?.detail || error?.response?.data?.message || error?.message || 'Failed to dead-letter messages';
      toast.error(`DLQ Error: ${errorDetail}`);
      console.error('Dead-letter error:', error);
    } finally {
      setIsDeadLettering(false);
    }
  };

  const handleGenerated = () => {
    onMessagesGenerated?.();
    // Cache invalidation now happens in MessageGeneratorModal - no delay needed
  };

  // Get the help text for DLQ button with explanation
  const getDLQHelpText = () => {
    if (!namespaceId) return 'Select a namespace first';
    if (entityType === 'topic') {
      if (!topicName || !subscriptionName) return 'Select a subscription first';
      return `Move up to 3 test msgs to DLQ`;
    }
    if (!queueName) return 'Select a queue first';
    return `Move up to 3 test msgs to DLQ`;
  };

  return (
    <>
      {/* FAB Menu Container */}
      <div className="fixed bottom-8 right-8 z-50" ref={menuRef}>
        {/* Expanded Menu */}
        <div
          className={`
            absolute bottom-16 right-0
            flex flex-col gap-2
            transition-all duration-200 ease-out
            ${isMenuOpen ? 'opacity-100 translate-y-0' : 'opacity-0 translate-y-4 pointer-events-none'}
          `}
        >
          {/* Refresh All Option */}
          <button
            onClick={() => { setIsMenuOpen(false); refreshAll(); }}
            className="
              flex items-center gap-3
              px-4 py-3
              bg-white hover:bg-green-50
              border border-gray-200 hover:border-green-300
              rounded-xl shadow-lg hover:shadow-xl
              transition-all duration-150
              group
            "
          >
            <div className="p-2 bg-green-100 rounded-lg group-hover:bg-green-200 transition-colors">
              <RefreshCw className="w-5 h-5 text-green-600" />
            </div>
            <div className="text-left">
              <div className="text-sm font-semibold text-gray-800">Refresh All</div>
              <div className="text-xs text-gray-500">Update queues & messages</div>
            </div>
          </button>

          {/* Dead-Letter Messages Option */}
          <button
            onClick={handleDeadLetter}
            disabled={!hasValidEntity || isDeadLettering}
            className={`
              flex items-center gap-3
              px-4 py-3
              border rounded-xl shadow-lg
              transition-all duration-150
              group
              ${!hasValidEntity 
                ? 'bg-gray-100 border-gray-200 cursor-not-allowed opacity-60'
                : 'bg-white hover:bg-red-50 border-gray-200 hover:border-red-300 hover:shadow-xl'
              }
            `}
            title="For testing: moves up to 3 messages from the active queue to the dead-letter queue"
          >
            <div className={`p-2 rounded-lg transition-colors ${
              !hasValidEntity 
                ? 'bg-gray-200' 
                : 'bg-red-100 group-hover:bg-red-200'
            }`}>
              <Skull className={`w-5 h-5 ${!hasValidEntity ? 'text-gray-400' : 'text-red-600'}`} />
            </div>
            <div className="text-left">
              <div className={`text-sm font-semibold ${!hasValidEntity ? 'text-gray-400' : 'text-gray-800'}`}>
                {isDeadLettering ? 'Moving...' : 'Test DLQ'}
              </div>
              <div className="text-xs text-gray-500 max-w-[180px]">
                {getDLQHelpText()}
              </div>
            </div>
          </button>

          {/* Generate Messages Option */}
          <button
            onClick={handleOpenGenerate}
            className="
              flex items-center gap-3
              px-4 py-3
              bg-white hover:bg-amber-50
              border border-gray-200 hover:border-amber-300
              rounded-xl shadow-lg hover:shadow-xl
              transition-all duration-150
              group
            "
          >
            <div className="p-2 bg-amber-100 rounded-lg group-hover:bg-amber-200 transition-colors">
              <Wand2 className="w-5 h-5 text-amber-600" />
            </div>
            <div className="text-left">
              <div className="text-sm font-semibold text-gray-800">Generate Messages</div>
              <div className="text-xs text-gray-500">Create realistic test data</div>
            </div>
          </button>

          {/* Send Message Option */}
          <button
            onClick={handleOpenSend}
            className="
              flex items-center gap-3
              px-4 py-3
              bg-white hover:bg-sky-50
              border border-gray-200 hover:border-sky-300
              rounded-xl shadow-lg hover:shadow-xl
              transition-all duration-150
              group
            "
          >
            <div className="p-2 bg-sky-100 rounded-lg group-hover:bg-sky-200 transition-colors">
              <Send className="w-5 h-5 text-sky-600" />
            </div>
            <div className="text-left">
              <div className="text-sm font-semibold text-gray-800">Send Message</div>
              <div className="text-xs text-gray-500">Send a single message</div>
            </div>
          </button>
        </div>

        {/* Main FAB Button */}
        <button
          onClick={() => setIsMenuOpen(!isMenuOpen)}
          className={`
            flex items-center justify-center
            w-14 h-14
            bg-gradient-to-br from-sky-500 to-sky-600 hover:from-sky-600 hover:to-sky-700
            text-white
            rounded-full shadow-lg hover:shadow-xl
            transition-all duration-200 ease-out
            ring-4 ring-sky-200 ring-offset-2
            ${isMenuOpen ? 'rotate-45' : 'rotate-0'}
          `}
          title={isMenuOpen ? 'Close menu' : 'Open message menu'}
        >
          {isMenuOpen ? (
            <X className="w-6 h-6" />
          ) : (
            <Plus className="w-6 h-6" />
          )}
        </button>
      </div>

      {/* Send Message Modal */}
      <SendMessageModal
        isOpen={activeModal === 'send'}
        onClose={() => setActiveModal(null)}
        onSend={handleSend}
        defaultNamespaceId={namespaceId}
        defaultQueueName={queueName}
      />

      {/* Message Generator Modal */}
      <MessageGeneratorModal
        isOpen={activeModal === 'generate'}
        onClose={() => setActiveModal(null)}
        defaultNamespaceId={namespaceId}
        defaultEntityName={entityType === 'topic' ? topicName : queueName}
        onGenerated={handleGenerated}
      />
    </>
  );
}
