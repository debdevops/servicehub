import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SendMessageModal } from '@/components/fab/SendMessageModal';

/**
 * Tests for SendMessageModal component
 * Coverage target: 80%+ (currently 0%)
 * Importance: HIGH - Core message sending feature
 */
describe('SendMessageModal', () => {
  const mockOnClose = vi.fn();
  const mockOnSend = vi.fn();

  const defaultProps = {
    isOpen: true,
    onClose: mockOnClose,
    onSend: mockOnSend,
    entityName: 'test-queue',
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Visibility ────────────────────────────────────────────────────────────

  it('renders nothing when isOpen is false', () => {
    const { container } = render(
      <SendMessageModal {...defaultProps} isOpen={false} />
    );
    expect(container.querySelector('[role="dialog"]')).not.toBeInTheDocument();
  });

  it('renders modal with message body field', () => {
    render(<SendMessageModal {...defaultProps} />);
    expect(screen.getByLabelText(/message body|body/i)).toBeInTheDocument();
  });

  // ── Message Body Input ────────────────────────────────────────────────────

  it('accepts JSON message body', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const bodyInput = screen.getByLabelText(/message body|body/i);
    const jsonBody = '{"id": 123, "name": "test"}';
    
    await user.type(bodyInput, jsonBody);
    
    expect(bodyInput).toHaveValue(jsonBody);
  });

  it('validates JSON format', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const bodyInput = screen.getByLabelText(/message body|body/i);
    await user.type(bodyInput, 'invalid json{');
    
    // Should show validation error
    await waitFor(() => {
      expect(screen.queryByText(/invalid.*json|json format/i)).toBeInTheDocument();
    });
  });

  // ── Custom Properties ─────────────────────────────────────────────────────

  it('allows adding custom properties', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const addPropertyButton = screen.getByRole('button', { name: /add.*property|add property/i });
    await user.click(addPropertyButton);
    
    const propertyKeyInput = screen.getByPlaceholderText(/key|property name/i);
    const propertyValueInput = screen.getByPlaceholderText(/value|property value/i);
    
    await user.type(propertyKeyInput, 'customKey');
    await user.type(propertyValueInput, 'customValue');
    
    expect(propertyKeyInput).toHaveValue('customKey');
    expect(propertyValueInput).toHaveValue('customValue');
  });

  it('removes property when remove button clicked', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    // Add property
    const addButton = screen.getByRole('button', { name: /add.*property/i });
    await user.click(addButton);
    
    // Remove it
    const removeButton = screen.getByRole('button', { name: /remove|delete|x/i });
    await user.click(removeButton);
    
    expect(screen.queryByPlaceholderText(/property name/i)).not.toBeInTheDocument();
  });

  // ── Headers and Correlation ───────────────────────────────────────────────

  it('allows setting correlation ID', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const correlationInput = screen.getByLabelText(/correlation.*id|correlation/i);
    const correlationId = 'corr-123-abc';
    
    await user.type(correlationInput, correlationId);
    
    expect(correlationInput).toHaveValue(correlationId);
  });

  it('allows setting message ID', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const messageIdInput = screen.getByLabelText(/message.*id|message id/i);
    const messageId = 'msg-456-def';
    
    await user.type(messageIdInput, messageId);
    
    expect(messageIdInput).toHaveValue(messageId);
  });

  // ── Form Submission ───────────────────────────────────────────────────────

  it('sends message with valid data', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const bodyInput = screen.getByLabelText(/message body|body/i);
    await user.type(bodyInput, '{"test": "data"}');
    
    const sendButton = screen.getByRole('button', { name: /send|submit/i });
    await user.click(sendButton);
    
    await waitFor(() => {
      expect(mockOnSend).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.any(String),
        })
      );
    });
  });

  it('disables send button with empty body', () => {
    render(<SendMessageModal {...defaultProps} />);
    
    const sendButton = screen.getByRole('button', { name: /send/i });
    expect(sendButton).toBeDisabled();
  });

  // ── Cancel and Close ──────────────────────────────────────────────────────

  it('closes modal on cancel', async () => {
    const user = userEvent.setup();
    render(<SendMessageModal {...defaultProps} />);
    
    const cancelButton = screen.getByRole('button', { name: /cancel/i });
    await user.click(cancelButton);
    
    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });

  it('closes on escape key', () => {
    render(<SendMessageModal {...defaultProps} />);
    
    fireEvent.keyDown(document, { key: 'Escape' });
    
    expect(mockOnClose).toHaveBeenCalled();
  });
});
