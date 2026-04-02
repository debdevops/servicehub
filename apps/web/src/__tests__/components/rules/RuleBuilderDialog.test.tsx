import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RuleBuilderDialog } from '@/components/rules/RuleBuilderDialog';
import type { CreateRuleRequest, RuleResponse } from '@/lib/api/rules';

const defaultProps = {
  open: true,
  onClose: vi.fn(),
  onSave: vi.fn(),
  isSaving: false,
};

describe('RuleBuilderDialog', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('renders dialog when open=true', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    expect(screen.getByText('Create Auto-Replay Rule')).toBeInTheDocument();
  });

  it('renders nothing when open=false', () => {
    render(<RuleBuilderDialog {...defaultProps} open={false} />);
    expect(screen.queryByText('Create Auto-Replay Rule')).not.toBeInTheDocument();
  });

  it('shows "Edit Auto-Replay Rule" title when editRule is provided', () => {
    const editRule: RuleResponse = {
      id: 'rule-1',
      name: 'My Rule',
      enabled: true,
      conditions: [{ field: 'DeadLetterReason', operator: 'Contains', value: 'timeout' }],
      action: { autoReplay: true, delaySeconds: 60, maxRetries: 3, exponentialBackoff: false },
      maxReplaysPerHour: 100,
      matchCount: 0,
      replayCount: 0,
      createdAt: new Date().toISOString(),
      lastModifiedAt: new Date().toISOString(),
    };
    render(<RuleBuilderDialog {...defaultProps} editRule={editRule} />);
    expect(screen.getByText('Edit Auto-Replay Rule')).toBeInTheDocument();
  });

  it('prepopulates name field when editing rule', () => {
    const editRule: RuleResponse = {
      id: 'rule-1',
      name: 'Timeout Handler',
      enabled: true,
      conditions: [{ field: 'DeadLetterReason', operator: 'Contains', value: 'timeout' }],
      action: { autoReplay: true, delaySeconds: 30, maxRetries: 5, exponentialBackoff: true },
      maxReplaysPerHour: 50,
      matchCount: 0,
      replayCount: 0,
      createdAt: new Date().toISOString(),
      lastModifiedAt: new Date().toISOString(),
    };
    render(<RuleBuilderDialog {...defaultProps} editRule={editRule} />);
    const nameInput = screen.getByPlaceholderText(/e.g., Database Timeouts/i);
    expect((nameInput as HTMLInputElement).value).toBe('Timeout Handler');
  });

  it('calls onClose when Cancel is clicked', () => {
    const onClose = vi.fn();
    render(<RuleBuilderDialog {...defaultProps} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onClose).toHaveBeenCalled();
  });

  it('Save button is disabled when name is empty', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save button is enabled when name and conditions are filled', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    fireEvent.change(screen.getByPlaceholderText(/e.g., Database Timeouts/i), {
      target: { value: 'My New Rule' },
    });
    fireEvent.change(screen.getByPlaceholderText(/value\.\.\.$/i), {
      target: { value: 'timeout' },
    });
    expect(screen.getByRole('button', { name: /save/i })).not.toBeDisabled();
  });

  it('calls onSave with correct payload when Save is clicked', () => {
    const onSave = vi.fn();
    render(<RuleBuilderDialog {...defaultProps} onSave={onSave} />);
    fireEvent.change(screen.getByPlaceholderText(/e.g., Database Timeouts/i), {
      target: { value: 'Test Rule' },
    });
    fireEvent.change(screen.getByPlaceholderText(/value\.\.\.$/i), {
      target: { value: 'connection reset' },
    });
    fireEvent.click(screen.getByRole('button', { name: /^save$/i }));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining<Partial<CreateRuleRequest>>({
        name: 'Test Rule',
        enabled: true,
        conditions: expect.arrayContaining([
          expect.objectContaining({ value: 'connection reset' }),
        ]),
      }),
    );
  });

  it('adds a new condition when "Add Condition" is clicked', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    const initialRows = screen.getAllByRole('combobox');
    fireEvent.click(screen.getByRole('button', { name: /add condition/i }));
    const newRows = screen.getAllByRole('combobox');
    expect(newRows.length).toBeGreaterThan(initialRows.length);
  });

  it('shows "Saving..." text when isSaving=true', () => {
    render(<RuleBuilderDialog {...defaultProps} isSaving />);
    expect(screen.getByText('Saving...')).toBeInTheDocument();
  });

  it('renders description input', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    expect(screen.getByPlaceholderText(/describe what this rule does/i)).toBeInTheDocument();
  });

  it('renders "Enable this rule" checkbox that is checked by default', () => {
    render(<RuleBuilderDialog {...defaultProps} />);
    const checkbox = screen.getByRole('checkbox', { name: /enable this rule/i });
    expect((checkbox as HTMLInputElement).checked).toBe(true);
  });

  it('applies initialConditions when provided', () => {
    render(
      <RuleBuilderDialog
        {...defaultProps}
        initialConditions={[
          { field: 'CorrelationId', operator: 'Equals', value: 'abc-123' },
        ]}
      />,
    );
    // The value input should be pre-filled
    const valueInput = screen.getByPlaceholderText(/value\.\.\.$/i);
    expect((valueInput as HTMLInputElement).value).toBe('abc-123');
  });
});
