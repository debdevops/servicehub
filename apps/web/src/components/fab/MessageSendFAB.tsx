import { useState } from 'react';
import { Send } from 'lucide-react';
import { SendMessageModal, type MessagePayload } from './SendMessageModal';
import toast from 'react-hot-toast';

// ============================================================================
// MessageSendFAB - Floating Action Button for sending messages
// ============================================================================

interface MessageSendFABProps {
  namespaceId?: string | null;
  queueName?: string | null;
  onMessageSent?: (payload: MessagePayload) => void;
}

export function MessageSendFAB({ namespaceId, queueName, onMessageSent }: MessageSendFABProps) {
  const [isExpanded, setIsExpanded] = useState(false);
  const [isModalOpen, setIsModalOpen] = useState(false);

  const handleSend = (payload: MessagePayload) => {
    // Phase 1: Mock implementation - just show toast
    const count = payload.messageCount;
    const entityDisplay = `${payload.entityType === 'topic' ? '📢' : '📥'} ${payload.entity}`;
    
    toast.success(
      count > 1 
        ? `Sent ${count} messages to ${entityDisplay}`
        : `Message sent to ${entityDisplay}`
    );

    // Callback for parent component
    onMessageSent?.(payload);
    
    // Close modal
    setIsModalOpen(false);
  };

  return (
    <>
      {/* FAB Button */}
      <button
        onClick={() => setIsModalOpen(true)}
        onMouseEnter={() => setIsExpanded(true)}
        onMouseLeave={() => setIsExpanded(false)}
        className={`
          fixed bottom-8 right-8 z-40
          flex items-center justify-center gap-2
          bg-primary-500 hover:bg-primary-600
          text-white font-medium
          rounded-full shadow-lg hover:shadow-xl
          transition-all duration-200 ease-out
          ${isExpanded ? 'px-6 h-14' : 'w-14 h-14'}
        `}
        title="Send Message"
      >
        <Send className="w-5 h-5 shrink-0" />
        {isExpanded && (
          <span className="whitespace-nowrap overflow-hidden">
            Send Message
          </span>
        )}
      </button>

      {/* Send Message Modal */}
      <SendMessageModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onSend={handleSend}
        defaultNamespaceId={namespaceId}
        defaultQueueName={queueName}
      />
    </>
  );
}
