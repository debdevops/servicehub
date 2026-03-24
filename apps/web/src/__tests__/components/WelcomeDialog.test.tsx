import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { WelcomeDialog, getStoredUser } from '@/components/WelcomeDialog';

// In-memory localStorage mock
const storage = new Map<string, string>();

beforeEach(() => {
  storage.clear();
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation((key) => storage.get(key) ?? null);
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation((key, value) => {
    storage.set(key, value);
  });
});

describe('getStoredUser', () => {
  it('returns null when nothing stored', () => {
    expect(getStoredUser()).toBeNull();
  });

  it('returns null for invalid JSON', () => {
    storage.set('servicehub_user', '{{broken');
    expect(getStoredUser()).toBeNull();
  });

  it('returns null if required fields missing', () => {
    storage.set('servicehub_user', JSON.stringify({ fullName: 'Jane' }));
    expect(getStoredUser()).toBeNull();
  });

  it('returns user when valid data stored', () => {
    storage.set('servicehub_user', JSON.stringify({ fullName: 'Jane', email: 'jane@co.com' }));
    const user = getStoredUser();
    expect(user).toEqual({ fullName: 'Jane', email: 'jane@co.com' });
  });
});

describe('WelcomeDialog', () => {
  it('renders when no stored user', () => {
    render(<WelcomeDialog />);
    expect(screen.getByText('Welcome to ServiceHub')).toBeInTheDocument();
  });

  it('does not render when user already stored', () => {
    storage.set('servicehub_user', JSON.stringify({ fullName: 'Jane', email: 'jane@co.com' }));
    const { container } = render(<WelcomeDialog />);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows error when fields are empty and submitted', () => {
    render(<WelcomeDialog />);
    fireEvent.submit(screen.getByText('Get Started').closest('form')!);
    expect(screen.getByText('Both fields are required.')).toBeInTheDocument();
  });

  it('shows error for invalid email', () => {
    render(<WelcomeDialog />);
    fireEvent.change(screen.getByPlaceholderText('e.g. Jane Smith'), { target: { value: 'Jane' } });
    fireEvent.change(screen.getByPlaceholderText('e.g. jane.smith@company.com'), { target: { value: 'not-an-email' } });
    fireEvent.submit(screen.getByText('Get Started').closest('form')!);
    expect(screen.getByText('Please enter a valid email address.')).toBeInTheDocument();
  });

  it('persists user and closes on valid submission', () => {
    render(<WelcomeDialog />);
    fireEvent.change(screen.getByPlaceholderText('e.g. Jane Smith'), { target: { value: 'Jane Smith' } });
    fireEvent.change(screen.getByPlaceholderText('e.g. jane.smith@company.com'), { target: { value: 'jane@company.com' } });
    fireEvent.submit(screen.getByText('Get Started').closest('form')!);

    expect(storage.get('servicehub_user')).toBeTruthy();
    const stored = JSON.parse(storage.get('servicehub_user')!);
    expect(stored.fullName).toBe('Jane Smith');
    expect(stored.email).toBe('jane@company.com');

    // Dialog should close
    expect(screen.queryByText('Welcome to ServiceHub')).not.toBeInTheDocument();
  });

  it('renders form labels', () => {
    render(<WelcomeDialog />);
    expect(screen.getByText('Full Name')).toBeInTheDocument();
    expect(screen.getByText('Email Address')).toBeInTheDocument();
  });
});
