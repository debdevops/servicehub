import { useState, useRef, useEffect } from 'react';
import { Send, Wand2, Plus, X } from 'lucide-react';
import { SendMessageModal, type MessagePayload } from './SendMessageModal';
import { MessageGeneratorModal } from './MessageGeneratorModal';
import toast from 'react-hot-toast';

// ============================================================================
// MessageFAB - Enhanced Floating Action Button with multiple actions
// ============================================================================

interface MessageFABProps {
  namespaceId?: string | null;
  queueName?: string | null;
  onMessageSent?: (payload: MessagePayload) => void;
  onMessagesGenerated?: () => void;
}

type ModalType = 'send' | 'generate' | null;

export function MessageFAB({ 
  namespaceId, 
  queueName, 
  onMessageSent,
  onMessagesGenerated,
}: MessageFABProps) {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [activeModal, setActiveModal] = useState<ModalType>(null);
  const menuRef = useRef<HTMLDivElement>(null);

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
  };

  const handleOpenSend = () => {
    setIsMenuOpen(false);
    setActiveModal('send');
  };

  const handleOpenGenerate = () => {
    setIsMenuOpen(false);
    setActiveModal('generate');
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
              bg-white hover:bg-primary-50
              border border-gray-200 hover:border-primary-300
              rounded-xl shadow-lg hover:shadow-xl
              transition-all duration-150
              group
            "
          >
            <div className="p-2 bg-primary-100 rounded-lg group-hover:bg-primary-200 transition-colors">
              <Send className="w-5 h-5 text-primary-600" />
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
            bg-primary-500 hover:bg-primary-600
            text-white
            rounded-full shadow-lg hover:shadow-xl
            transition-all duration-200 ease-out
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
        defaultEntityName={queueName}
        onGenerated={onMessagesGenerated}
      />
    </>
  );
}
