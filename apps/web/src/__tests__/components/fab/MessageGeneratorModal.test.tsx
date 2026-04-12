import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MessageGeneratorModal } from '@/components/fab/MessageGeneratorModal';

/**
 * Tests for MessageGeneratorModal component
 * Coverage target: 80%+ (currently 0%)
 * Importance: HIGH - Core test data generation feature
 */
describe('MessageGeneratorModal', () => {
  const mockOnClose = vi.fn();
  const mockOnGenerate = vi.fn();

  const defaultProps = {
    isOpen: true,
    onClose: mockOnClose,
    onGenerate: mockOnGenerate,
    entityName: 'test-queue',
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── Visibility ────────────────────────────────────────────────────────────

  it('renders nothing when isOpen is false', () => {
    const { container } = render(
      <MessageGeneratorModal {...defaultProps} isOpen={false} />
    );
    expect(container.querySelector('[role="dialog"]')).not.toBeInTheDocument();
  });

  it('renders modal title when isOpen is true', () => {
    render(<MessageGeneratorModal {...defaultProps} />);
    expect(screen.getByText(/generate test messages/i)).toBeInTheDocument();
  });

  // ── Scenario Selection ────────────────────────────────────────────────────

  it('displays all scenario options', () => {
    render(<MessageGeneratorModal {...defaultProps} />);
    
    expect(screen.getByText(/orders/i)).toBeInTheDocument();
    expect(screen.getByText(/payments/i)).toBeInTheDocument();
    expect(screen.getByText(/notifications/i)).toBeInTheDocument();
  });

  it('selects a scenario when clicked', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const orderButton = screen.getByRole('button', { name: /orders/i });
    await user.click(orderButton);
    
    expect(orderButton).toHaveClass('selected');
  });

  // ── Volume Configuration ──────────────────────────────────────────────────

  it('allows setting message count', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const volumeInput = screen.getByLabelText(/volume|count|messages/i);
    await user.clear(volumeInput);
    await user.type(volumeInput, '50');
    
    expect(volumeInput).toHaveValue(50);
  });

  it('enforces minimum message count', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const volumeInput = screen.getByLabelText(/volume|count|messages/i);
    await user.clear(volumeInput);
    await user.type(volumeInput, '1');
    
    await waitFor(() => {
      expect(screen.queryByText(/minimum.*30/i)).toBeInTheDocument();
    });
  });

  it('enforces maximum message count', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const volumeInput = screen.getByLabelText(/volume|count|messages/i);
    await user.clear(volumeInput);
    await user.type(volumeInput, '300');
    
    await waitFor(() => {
      expect(screen.queryByText(/maximum.*200/i)).toBeInTheDocument();
    });
  });

  // ── Anomaly Configuration ─────────────────────────────────────────────────

  it('allows setting anomaly rate', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const anomalySlider = screen.getByLabelText(/anomaly|error rate/i);
    fireEvent.change(anomalySlider, { target: { value: '25' } });
    
    expect(anomalySlider).toHaveValue('25');
  });

  it('shows anomaly rate percentage', () => {
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const anomalySlider = screen.getByLabelText(/anomaly|error rate/i);
    fireEvent.change(anomalySlider, { target: { value: '50' } });
    
    expect(screen.getByText(/50%/)).toBeInTheDocument();
  });

  // ── Form Submission ───────────────────────────────────────────────────────

  it('calls onGenerate with correct parameters', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    // Select scenario
    const orderButton = screen.getByRole('button', { name: /orders/i });
    await user.click(orderButton);
    
    // Set volume
    const volumeInput = screen.getByLabelText(/volume|count|messages/i);
    await user.clear(volumeInput);
    await user.type(volumeInput, '50');
    
    // Set anomaly
    const anomalySlider = screen.getByLabelText(/anomaly|error rate/i);
    fireEvent.change(anomalySlider, { target: { value: '20' } });
    
    // Submit
    const generateButton = screen.getByRole('button', { name: /generate/i });
    await user.click(generateButton);
    
    await waitFor(() => {
      expect(mockOnGenerate).toHaveBeenCalledWith(
        expect.objectContaining({
          scenario: expect.any(String),
          count: 50,
          anomalyRate: 20,
        })
      );
    });
  });

  it('disables generate button when form is invalid', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    // Try to submit with invalid data
    const generateButton = screen.getByRole('button', { name: /generate/i });
    
    // Button should be disabled if no scenario selected
    expect(generateButton).toBeDisabled();
  });

  // ── Cancel and Close ──────────────────────────────────────────────────────

  it('calls onClose when cancel button is clicked', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const cancelButton = screen.getByRole('button', { name: /cancel/i });
    await user.click(cancelButton);
    
    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when close button is clicked', async () => {
    const user = userEvent.setup();
    render(<MessageGeneratorModal {...defaultProps} />);
    
    const closeButton = screen.getByRole('button', { name: /close/i });
    await user.click(closeButton);
    
    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when escape key is pressed', async () => {
    render(<MessageGeneratorModal {...defaultProps} />);
    
    fireEvent.keyDown(document, { key: 'Escape', code: 'Escape' });
    
    expect(mockOnClose).toHaveBeenCalled();
  });
});
