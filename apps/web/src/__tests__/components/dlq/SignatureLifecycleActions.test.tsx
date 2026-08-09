import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SignatureLifecycleActions } from '@/components/dlq/SignatureLifecycleActions';

function noop() {}

describe('SignatureLifecycleActions', () => {
  it('Active: shows Mark Resolved, Suppress, Archive — not Reopen', () => {
    render(<SignatureLifecycleActions status="Active" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    expect(screen.getByText('Mark Resolved')).toBeInTheDocument();
    expect(screen.getByText('Suppress')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
    expect(screen.queryByText('Reopen')).not.toBeInTheDocument();
  });

  it('Resolved: shows Reopen, Suppress, Archive — not Mark Resolved', () => {
    render(<SignatureLifecycleActions status="Resolved" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    expect(screen.getByText('Reopen')).toBeInTheDocument();
    expect(screen.getByText('Suppress')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
    expect(screen.queryByText('Mark Resolved')).not.toBeInTheDocument();
  });

  it('Reopened: shows Mark Resolved, Suppress, Archive', () => {
    render(<SignatureLifecycleActions status="Reopened" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    expect(screen.getByText('Mark Resolved')).toBeInTheDocument();
    expect(screen.getByText('Suppress')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
  });

  it('Suppressed: shows Reopen and Archive — not Suppress', () => {
    render(<SignatureLifecycleActions status="Suppressed" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    expect(screen.getByText('Reopen')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
    expect(screen.queryByText('Suppress')).not.toBeInTheDocument();
    expect(screen.queryByText('Mark Resolved')).not.toBeInTheDocument();
  });

  it('Archived: shows only a static Archived label, no action buttons', () => {
    render(<SignatureLifecycleActions status="Archived" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    expect(screen.getByText('Archived')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('invokes the matching callback on click', () => {
    const onResolve = vi.fn();
    render(<SignatureLifecycleActions status="Active" onResolve={onResolve} onReopen={noop} onSuppress={noop} onArchive={noop} />);
    screen.getByText('Mark Resolved').click();
    expect(onResolve).toHaveBeenCalledOnce();
  });

  it('disables buttons when pending', () => {
    render(<SignatureLifecycleActions status="Active" onResolve={noop} onReopen={noop} onSuppress={noop} onArchive={noop} pending />);
    expect(screen.getByText('Mark Resolved')).toBeDisabled();
    expect(screen.getByText('Suppress')).toBeDisabled();
    expect(screen.getByText('Archive')).toBeDisabled();
  });
});
