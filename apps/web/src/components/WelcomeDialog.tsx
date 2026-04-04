import { useState, useEffect } from 'react';
import { User, Mail, ArrowRight, Lock } from 'lucide-react';

// ============================================================================
// WelcomeDialog - Optionally collects user identity on first visit
// Persists to localStorage so it only shows once per browser
// ============================================================================

const STORAGE_KEY = 'servicehub_user';
const DISMISSED_KEY = 'servicehub_dismissed';

export interface ServiceHubUser {
  fullName: string;
  email: string;
}

/** Read persisted user identity from localStorage (null if not set). */
// eslint-disable-next-line react-refresh/only-export-components
export function getStoredUser(): ServiceHubUser | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed?.fullName && parsed?.email) return parsed as ServiceHubUser;
    return null;
  } catch {
    return null;
  }
}

export function WelcomeDialog() {
  const [isOpen, setIsOpen] = useState(false);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');

  useEffect(() => {
    const dismissed = localStorage.getItem(DISMISSED_KEY) === 'true';
    if (!getStoredUser() && !dismissed) {
      setIsOpen(true);
    }
  }, []);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    const trimmedName = fullName.trim();
    const trimmedEmail = email.trim();

    if (!trimmedName || !trimmedEmail) {
      setError('Both fields are required.');
      return;
    }

    // Basic email validation
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
      setError('Please enter a valid email address.');
      return;
    }

    const user: ServiceHubUser = { fullName: trimmedName, email: trimmedEmail };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    setIsOpen(false);
  };

  const handleSkip = () => {
    localStorage.setItem(DISMISSED_KEY, 'true');
    setIsOpen(false);
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center">
      {/* Backdrop — no dismiss on click */}
      <div className="absolute inset-0 bg-black/50" aria-hidden="true" />

      {/* Dialog */}
      <div
        className="relative bg-white rounded-xl shadow-2xl w-full max-w-md overflow-hidden"
        role="dialog"
        aria-modal="true"
        aria-labelledby="welcome-dialog-title"
      >
        {/* Header */}
        <div className="px-6 pt-6 pb-2 text-center">
          <div className="w-14 h-14 bg-primary-50 border border-primary-100 rounded-full flex items-center justify-center mx-auto mb-4">
            <User className="w-7 h-7 text-primary-600" />
          </div>
          <h2 id="welcome-dialog-title" className="text-xl font-semibold text-gray-900">
            Welcome to ServiceHub
          </h2>
          <p className="text-sm text-gray-500 mt-1">
            Save your name so replays and actions are attributed to you in the audit log. Takes 10 seconds — or skip below.
          </p>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="px-6 pb-4 pt-4 space-y-4">
          <div>
            <label htmlFor="welcome-name" className="block text-sm font-medium text-gray-700 mb-1">
              Full Name
            </label>
            <div className="relative">
              <User className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                id="welcome-name"
                type="text"
                autoFocus
                maxLength={128}
                value={fullName}
                onChange={e => setFullName(e.target.value)}
                placeholder="e.g. Jane Smith"
                className="w-full pl-10 pr-4 py-2.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
          </div>

          <div>
            <label htmlFor="welcome-email" className="block text-sm font-medium text-gray-700 mb-1">
              Email Address
            </label>
            <div className="relative">
              <Mail className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                id="welcome-email"
                type="email"
                maxLength={256}
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="e.g. jane.smith@company.com"
                className="w-full pl-10 pr-4 py-2.5 border border-gray-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
            <p className="mt-1.5 flex items-center gap-1.5 text-xs text-gray-400">
              <Lock className="w-3 h-3 shrink-0" />
              Stored in your browser only — we never receive your data.
            </p>
          </div>

          {error && (
            <p className="text-sm text-red-600">{error}</p>
          )}

          <button
            type="submit"
            className="w-full flex items-center justify-center gap-2 bg-primary-600 hover:bg-primary-700 text-white font-medium py-2.5 rounded-lg transition-colors"
          >
            Get Started
            <ArrowRight className="w-4 h-4" />
          </button>
        </form>

        {/* Skip */}
        <div className="px-6 pb-6 text-center border-t border-gray-100 pt-4">
          <button
            type="button"
            onClick={handleSkip}
            className="text-sm text-gray-400 hover:text-gray-600 transition-colors"
          >
            Continue without saving →
          </button>
        </div>
      </div>
    </div>
  );
}
