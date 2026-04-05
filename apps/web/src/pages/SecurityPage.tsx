import { Link } from 'react-router-dom';
import {
  Shield,
  Lock,
  ExternalLink,
  Server,
  Monitor,
  ArrowRight,
  CheckCircle,
  Eye,
  FileCode,
  AlertTriangle,
} from 'lucide-react';

const GITHUB_BASE =
  'https://github.com/debdevops/servicehub/blob/main/services/api/src';

const VERIFY_LINKS = [
  {
    label: 'ConnectionStringProtector.cs',
    description: 'AES-256-GCM encryption and decryption of connection strings at rest.',
    href: `${GITHUB_BASE}/ServiceHub.Infrastructure/Security/ConnectionStringProtector.cs`,
  },
  {
    label: 'LogRedactor.cs',
    description: 'Strips SharedAccessKey and all secret values from every log entry before writing.',
    href: `${GITHUB_BASE}/ServiceHub.Infrastructure/Security/LogRedactor.cs`,
  },
  {
    label: 'NamespacesController.cs — MapToResponse',
    description: 'Confirms the API response DTO has no ConnectionString field — the encrypted value never leaves the server.',
    href: `${GITHUB_BASE}/ServiceHub.Api/Controllers/V1/NamespacesController.cs`,
  },
];

const WHAT_WE_PROTECT = [
  {
    icon: Lock,
    title: 'Connection strings',
    color: 'bg-green-100 text-green-700',
    points: [
      'Encrypted with AES-256-GCM immediately on receipt',
      'Encryption key lives only in Azure App Service configuration — never on disk',
      'Plaintext connection string is never written to disk, never logged, never returned to your browser after the initial POST',
    ],
  },
  {
    icon: Eye,
    title: 'Application logs',
    color: 'bg-blue-100 text-blue-700',
    points: [
      'LogRedactor strips SharedAccessKey and all secret-bearing values before any log is written',
      'If an exception is logged while processing a connection string, the key value is replaced with [REDACTED]',
      'No message content is ever included in server logs',
    ],
  },
  {
    icon: FileCode,
    title: 'API responses',
    color: 'bg-purple-100 text-purple-700',
    points: [
      'The NamespacesController MapToResponse method does not include ConnectionString in any response DTO',
      'Clients receive: ID, display name, environment, permissions, and timestamps — nothing sensitive',
      'The encrypted blob stays server-side only',
    ],
  },
  {
    icon: Monitor,
    title: 'Browser storage',
    color: 'bg-amber-100 text-amber-700',
    points: [
      'No connection strings are written to localStorage, sessionStorage, cookies, or IndexedDB',
      'The only data stored in your browser is your saved namespace IDs (not the credentials)',
      'Clearing browser storage does not affect your connection credentials',
    ],
  },
];

export function SecurityPage() {
  return (
    <div className="flex-1 overflow-auto bg-gradient-to-b from-white to-gray-50">
      <div className="max-w-3xl mx-auto px-6 py-10">

        {/* ══ HERO ══════════════════════════════════════════════════════ */}
        <div className="mb-10">
          <div className="flex items-center gap-3 mb-3">
            <div className="w-10 h-10 bg-green-100 rounded-xl flex items-center justify-center">
              <Shield className="w-5 h-5 text-green-600" />
            </div>
            <div>
              <h1 className="text-2xl font-bold text-gray-900 leading-tight">
                Security &amp; privacy
              </h1>
              <p className="text-sm text-gray-500">
                How ServiceHub handles your credentials and data
              </p>
            </div>
          </div>
          <p className="text-sm text-gray-600 leading-relaxed mt-4">
            We understand that pasting an Azure Service Bus connection string into a web app
            is a significant trust decision. This page explains exactly what ServiceHub stores,
            what it never touches, and where you can verify every claim directly in the
            open-source code.
          </p>
        </div>

        {/* ══ ARCHITECTURE FLOW ═════════════════════════════════════════ */}
        <div className="mb-10">
          <h2 className="text-base font-semibold text-gray-900 mb-4">
            How your data moves through ServiceHub
          </h2>
          <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-6">
            {/* Flow diagram */}
            <div className="flex flex-col sm:flex-row items-center justify-between gap-4 mb-6">
              {/* Browser */}
              <div className="flex flex-col items-center text-center">
                <div className="w-12 h-12 bg-blue-50 border border-blue-200 rounded-xl flex items-center justify-center mb-2">
                  <Monitor className="w-6 h-6 text-blue-600" />
                </div>
                <span className="text-xs font-semibold text-gray-800">Your browser</span>
              </div>

              {/* Arrow 1 */}
              <div className="flex flex-col items-center gap-1">
                <ArrowRight className="w-5 h-5 text-gray-400 rotate-0 sm:rotate-0 rotate-90" />
                <span className="text-[10px] font-medium text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full">
                  HTTPS + SPA token
                </span>
              </div>

              {/* ServiceHub server */}
              <div className="flex flex-col items-center text-center">
                <div className="w-12 h-12 bg-green-50 border border-green-200 rounded-xl flex items-center justify-center mb-2">
                  <Server className="w-6 h-6 text-green-600" />
                </div>
                <span className="text-xs font-semibold text-gray-800">ServiceHub server</span>
                <span className="text-[10px] text-gray-400">.NET 10 API</span>
              </div>

              {/* Arrow 2 */}
              <div className="flex flex-col items-center gap-1">
                <ArrowRight className="w-5 h-5 text-gray-400" />
                <span className="text-[10px] font-medium text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full">
                  Azure SDK
                </span>
              </div>

              {/* Service Bus */}
              <div className="flex flex-col items-center text-center">
                <div className="w-12 h-12 bg-blue-50 border border-blue-200 rounded-xl flex items-center justify-center mb-2">
                  <span className="text-xl">☁️</span>
                </div>
                <span className="text-xs font-semibold text-gray-800">Your Service Bus</span>
                <span className="text-[10px] text-gray-400">Azure</span>
              </div>
            </div>

            {/* Annotations */}
            <div className="space-y-2 border-t border-gray-100 pt-4">
              <div className="flex items-start gap-2">
                <CheckCircle className="w-4 h-4 text-green-500 shrink-0 mt-0.5" />
                <p className="text-xs text-gray-600">
                  <strong>Connection string:</strong> Encrypted with AES-256-GCM immediately on the server.
                  The encrypted blob is stored. The plaintext is discarded. It is never returned to the browser.
                </p>
              </div>
              <div className="flex items-start gap-2">
                <CheckCircle className="w-4 h-4 text-green-500 shrink-0 mt-0.5" />
                <p className="text-xs text-gray-600">
                  <strong>Message content:</strong> Read transiently from Azure Service Bus via the SDK to
                  display in your browser. Never stored, never logged, never indexed by ServiceHub.
                </p>
              </div>
              <div className="flex items-start gap-2">
                <CheckCircle className="w-4 h-4 text-green-500 shrink-0 mt-0.5" />
                <p className="text-xs text-gray-600">
                  <strong>Browser traffic:</strong> All requests use HTTPS and a short-lived HMAC-signed
                  SPA token — no raw API keys are exposed to the browser.
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* ══ WHAT WE PROTECT ══════════════════════════════════════════ */}
        <div className="mb-10">
          <h2 className="text-base font-semibold text-gray-900 mb-4">What we protect</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {WHAT_WE_PROTECT.map(({ icon: Icon, title, color, points }) => (
              <div key={title} className="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
                <div className="flex items-center gap-2 mb-3">
                  <div className={`w-7 h-7 rounded-lg flex items-center justify-center ${color}`}>
                    <Icon className="w-4 h-4" />
                  </div>
                  <span className="text-sm font-semibold text-gray-900">{title}</span>
                </div>
                <ul className="space-y-1.5">
                  {points.map((point) => (
                    <li key={point} className="flex items-start gap-1.5 text-xs text-gray-600">
                      <span className="w-1 h-1 rounded-full bg-gray-400 mt-1.5 shrink-0" />
                      {point}
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        </div>

        {/* ══ VERIFY YOURSELF ══════════════════════════════════════════ */}
        <div className="mb-10">
          <h2 className="text-base font-semibold text-gray-900 mb-1">Verify it yourself</h2>
          <p className="text-sm text-gray-500 mb-4">
            ServiceHub is fully open source. Every security claim on this page has a direct
            link to the relevant source file on GitHub.
          </p>
          <div className="space-y-3">
            {VERIFY_LINKS.map(({ label, description, href }) => (
              <a
                key={label}
                href={href}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-start gap-3 bg-white rounded-xl border border-gray-200 shadow-sm p-4 hover:border-primary-300 hover:shadow-md transition-all group"
              >
                <div className="w-8 h-8 bg-gray-50 border border-gray-200 rounded-lg flex items-center justify-center shrink-0 group-hover:bg-primary-50 group-hover:border-primary-200 transition-colors">
                  <FileCode className="w-4 h-4 text-gray-500 group-hover:text-primary-600" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-gray-900 group-hover:text-primary-700 font-mono">
                      {label}
                    </span>
                    <ExternalLink className="w-3 h-3 text-gray-400 group-hover:text-primary-500 shrink-0" />
                  </div>
                  <p className="text-xs text-gray-500 mt-0.5">{description}</p>
                </div>
              </a>
            ))}
          </div>
        </div>

        {/* ══ PREFER ZERO TRUST / SELF-HOST ════════════════════════════ */}
        <div className="mb-10">
          <h2 className="text-base font-semibold text-gray-900 mb-1">
            Prefer zero trust in third-party infrastructure?
          </h2>
          <p className="text-sm text-gray-500 mb-4">
            If your security policy does not allow connecting production or sensitive
            Service Bus namespaces to a hosted third-party app — that is the right call.
            ServiceHub is designed to be self-hosted.
          </p>
          <div className="bg-blue-50 border border-blue-200 rounded-xl p-5">
            <div className="flex items-start gap-3">
              <div className="w-8 h-8 bg-blue-100 border border-blue-200 rounded-lg flex items-center justify-center shrink-0">
                <Server className="w-4 h-4 text-blue-600" />
              </div>
              <div>
                <p className="text-sm font-semibold text-blue-900">
                  Run ServiceHub in your own Azure subscription
                </p>
                <p className="text-xs text-blue-700 mt-1 leading-relaxed">
                  Deploy the .NET 10 API + React frontend to your own Azure App Service.
                  Your connection strings are encrypted with a key only you control.
                  No data ever leaves your infrastructure.
                  The self-hosting guide walks through a complete deployment in under 10 minutes.
                </p>
                <a
                  href="https://github.com/debdevops/servicehub#-quick-start"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1.5 text-xs text-blue-700 font-medium hover:text-blue-900 hover:underline mt-3"
                >
                  <ExternalLink className="w-3 h-3" />
                  Self-hosting guide on GitHub
                </a>
              </div>
            </div>
          </div>
        </div>

        {/* ══ RESPONSIBLE DISCLOSURE ════════════════════════════════════ */}
        <div className="mb-6">
          <div className="bg-amber-50 border border-amber-200 rounded-xl p-5">
            <div className="flex items-start gap-3">
              <div className="w-8 h-8 bg-amber-100 border border-amber-200 rounded-lg flex items-center justify-center shrink-0">
                <AlertTriangle className="w-4 h-4 text-amber-600" />
              </div>
              <div>
                <p className="text-sm font-semibold text-amber-900">Found a security issue?</p>
                <p className="text-xs text-amber-700 mt-1 leading-relaxed">
                  Please do not open a public GitHub issue for security vulnerabilities.
                  Open a GitHub issue with the title prefixed{' '}
                  <code className="bg-amber-100 px-1 rounded font-mono">[SECURITY]</code>{' '}
                  — it will be treated as a private disclosure and acknowledged within 48 hours.
                  We follow responsible disclosure: we will coordinate a fix and credit you
                  when the patch is released.
                </p>
                <a
                  href="https://github.com/debdevops/servicehub/issues/new?title=%5BSECURITY%5D"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1.5 text-xs text-amber-700 font-medium hover:text-amber-900 hover:underline mt-3"
                >
                  <ExternalLink className="w-3 h-3" />
                  Report a security issue on GitHub
                </a>
              </div>
            </div>
          </div>
        </div>

        {/* Footer nav */}
        <div className="flex flex-wrap items-center gap-4 pt-4 border-t border-gray-100 text-xs text-gray-500">
          <Link to="/connect" className="hover:text-gray-900 hover:underline">
            ← Back to Connect
          </Link>
          <Link to="/help" className="hover:text-gray-900 hover:underline">
            Help &amp; documentation
          </Link>
          <a
            href="https://github.com/debdevops/servicehub"
            target="_blank"
            rel="noopener noreferrer"
            className="hover:text-gray-900 hover:underline"
          >
            GitHub repository
          </a>
        </div>
      </div>
    </div>
  );
}
