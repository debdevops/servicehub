import { Link } from 'react-router-dom';
import {
  Search,
  Zap,
  Lock,
  BarChart3,
  Github,
  ArrowRight,
  Check,
} from 'lucide-react';

export function WelcomePage() {
  const features = [
    {
      icon: Search,
      title: 'Search & Filter Messages',
      description: 'Find exactly what you need in your DLQs with powerful search and filtering — no more dead ends.',
    },
    {
      icon: Zap,
      title: 'Replay Messages Instantly',
      description: 'Set rules to automatically replay messages or trigger them manually with a single click.',
    },
    {
      icon: Lock,
      title: 'Enterprise Security',
      description: 'End-to-end encryption. Connection strings never leave your server. Full audit trail included.',
    },
    {
      icon: BarChart3,
      title: 'Insights & Analytics',
      description: 'Understand message patterns, error trends, and queue health at a glance with client-side AI analysis.',
    },
  ];

  return (
    <div className="flex flex-col min-h-screen bg-gradient-to-b from-white via-primary-50/30 to-white">
      {/* Header */}
      <header className="fixed top-0 w-full bg-white/80 backdrop-blur-sm border-b border-gray-200 z-50">
        <div className="max-w-6xl mx-auto px-6 py-4 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-8 h-8 bg-primary-600 rounded-lg flex items-center justify-center">
              <Search className="w-5 h-5 text-white" />
            </div>
            <span className="font-bold text-gray-900">ServiceHub</span>
          </div>
          <nav className="flex items-center gap-6">
            <a
              href="https://github.com/debdevops/servicehub"
              target="_blank"
              rel="noopener noreferrer"
              className="flex items-center gap-2 text-sm text-gray-600 hover:text-gray-900 transition-colors"
            >
              <Github className="w-4 h-4" />
              GitHub
            </a>
          </nav>
        </div>
      </header>

      {/* Hero Section */}
      <section className="pt-32 pb-20 px-6">
        <div className="max-w-4xl mx-auto text-center">
          <div className="inline-block mb-6 px-4 py-2 bg-primary-100 text-primary-700 rounded-full text-sm font-medium">
            ✨ Free Hosted Demo Available
          </div>

          <h1 className="text-5xl md:text-6xl font-bold text-gray-900 mb-6 leading-tight">
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary-600 to-blue-600 block mb-2">
              Debug Azure Service Bus
            </span>
            <span>Dead-Letter Queues in Minutes</span>
          </h1>

          <p className="text-xl text-gray-600 mb-10 max-w-2xl mx-auto leading-relaxed">
            ServiceHub makes debugging Azure Service Bus DLQs effortless. Search messages, replay automatically,
            and understand your queue health — without leaving your browser.
          </p>

          {/* CTA Buttons */}
          <div className="flex flex-col sm:flex-row items-center justify-center gap-4 mb-12">
            <Link
              to="/connect"
              className="inline-flex items-center gap-2 px-8 py-3 bg-primary-600 text-white font-semibold rounded-lg hover:bg-primary-700 transition-colors shadow-lg hover:shadow-xl"
            >
              Try Free Demo
              <ArrowRight className="w-4 h-4" />
            </Link>
            <a
              href="https://github.com/debdevops/servicehub"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-8 py-3 bg-white text-primary-600 font-semibold rounded-lg border-2 border-primary-600 hover:bg-primary-50 transition-colors"
            >
              <Github className="w-4 h-4" />
              View on GitHub
            </a>
          </div>

          {/* Trust Indicators */}
          <div className="flex items-center justify-center gap-8 text-sm text-gray-600 pb-12 border-b border-gray-200">
            <div>
              <span className="font-semibold text-gray-900">100%</span> Open Source
            </div>
            <div className="w-px h-4 bg-gray-300" />
            <div>
              <span className="font-semibold text-gray-900">Enterprise</span> Security
            </div>
          </div>
        </div>
      </section>

      {/* Features Grid */}
      <section className="py-20 px-6 bg-white/40 backdrop-blur-sm">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-16">
            Everything You Need to Master Your DLQs
          </h2>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
            {features.map(({ icon: Icon, title, description }, idx) => (
              <div
                key={idx}
                className="p-6 bg-white rounded-xl border border-gray-200 shadow-sm hover:shadow-md hover:border-primary-300 transition-all"
              >
                <div className="w-12 h-12 bg-primary-100 rounded-lg flex items-center justify-center mb-4">
                  <Icon className="w-6 h-6 text-primary-600" />
                </div>
                <h3 className="text-lg font-semibold text-gray-900 mb-2">{title}</h3>
                <p className="text-gray-600">{description}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Why ServiceHub */}
      <section className="py-20 px-6">
        <div className="max-w-4xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 mb-12 text-center">
            Why Choose ServiceHub?
          </h2>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-12">
            <div>
              <h3 className="text-xl font-bold text-gray-900 mb-6">Better Than Azure Portal</h3>
              <ul className="space-y-3">
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Search & filter across thousands of messages</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Auto-replay rules with pattern matching</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Dead-letter history & analytics</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Zero learning curve for Azure teams</span>
                </li>
              </ul>
            </div>

            <div>
              <h3 className="text-xl font-bold text-gray-900 mb-6">Built for DevOps & SREs</h3>
              <ul className="space-y-3">
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Works on any OS — no install needed</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Multi-region & multi-namespace support</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Enterprise-grade security & encryption</span>
                </li>
                <li className="flex items-start gap-3">
                  <Check className="w-5 h-5 text-green-600 shrink-0 mt-0.5" />
                  <span className="text-gray-600">Self-host or use our free demo</span>
                </li>
              </ul>
            </div>
          </div>
        </div>
      </section>

      {/* Social Proof / Highlight */}
      <section className="py-16 px-6 bg-primary-50/50">
        <div className="max-w-3xl mx-auto text-center">
          <h3 className="text-2xl font-bold text-gray-900 mb-6">What's Included in v3.1.0</h3>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            {[
              { label: 'DLQ Intelligence', emoji: '🔍' },
              { label: 'Auto-Replay', emoji: '⚡' },
              { label: 'Scheduled Messages', emoji: '⏱️' },
              { label: 'Correlation Explorer', emoji: '🔗' },
              { label: 'Live Health Monitor', emoji: '💚' },
              { label: 'Pattern Analysis', emoji: '📊' },
              { label: 'Multi-Namespace', emoji: '🌐' },
              { label: 'Enterprise Security', emoji: '🔐' },
            ].map(({ label, emoji }) => (
              <div key={label} className="flex flex-col items-center gap-2 p-3 bg-white rounded-lg">
                <span className="text-2xl">{emoji}</span>
                <span className="text-xs font-medium text-gray-700">{label}</span>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Final CTA */}
      <section className="py-20 px-6">
        <div className="max-w-3xl mx-auto bg-gradient-to-r from-primary-600 to-blue-600 rounded-2xl p-12 text-white text-center shadow-xl">
          <h2 className="text-3xl font-bold mb-4">Ready to Debug Your DLQs Like a Pro?</h2>
          <p className="text-lg mb-8 text-white/90">
            No credit card needed. No setup required. Try the demo right now.
          </p>
          <Link
            to="/connect"
            className="inline-flex items-center gap-2 px-8 py-4 bg-white text-primary-600 font-bold rounded-lg hover:bg-gray-100 transition-colors shadow-lg"
          >
            Launch Free Demo
            <ArrowRight className="w-5 h-5" />
          </Link>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-gray-200 py-12 px-6 bg-gray-50">
        <div className="max-w-6xl mx-auto">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-8 mb-8">
            <div>
              <h4 className="font-semibold text-gray-900 mb-4">Product</h4>
              <ul className="space-y-2 text-sm text-gray-600">
                <li>
                  <a href="https://github.com/debdevops/servicehub/blob/main/README.md" className="hover:text-primary-600">
                    Documentation
                  </a>
                </li>
                <li>
                  <a href="https://github.com/debdevops/servicehub/tree/main/self-hosting" className="hover:text-primary-600">
                    Self-Hosting
                  </a>
                </li>
                <li>
                  <Link to="/security" className="hover:text-primary-600">
                    Security & Privacy
                  </Link>
                </li>
              </ul>
            </div>

            <div>
              <h4 className="font-semibold text-gray-900 mb-4">Community</h4>
              <ul className="space-y-2 text-sm text-gray-600">
                <li>
                  <a
                    href="https://github.com/debdevops/servicehub"
                    className="hover:text-primary-600 flex items-center gap-2"
                  >
                    <Github className="w-4 h-4" />
                    GitHub
                  </a>
                </li>
                <li>
                  <a href="https://github.com/debdevops/servicehub/issues" className="hover:text-primary-600">
                    Report Issues
                  </a>
                </li>
                <li>
                  <a href="https://github.com/debdevops/servicehub/discussions" className="hover:text-primary-600">
                    Discussions
                  </a>
                </li>
              </ul>
            </div>

            <div>
              <h4 className="font-semibold text-gray-900 mb-4">Legal</h4>
              <ul className="space-y-2 text-sm text-gray-600">
                <li>
                  <a
                    href="https://github.com/debdevops/servicehub/blob/main/SECURITY.md"
                    className="hover:text-primary-600"
                  >
                    Security Policy
                  </a>
                </li>
                <li>
                  <a href="https://github.com/debdevops/servicehub/blob/main/LICENSE" className="hover:text-primary-600">
                    License (Apache 2.0)
                  </a>
                </li>
              </ul>
            </div>
          </div>

          <div className="border-t border-gray-200 pt-8 text-center text-sm text-gray-600">
            <p>
              ServiceHub is open source and free to use. Made with ❤️ by{' '}
              <a href="https://github.com/debdevops" className="text-primary-600 hover:underline">
                Debasis
              </a>
            </p>
            <p className="mt-2">
              © 2026 ServiceHub. All rights reserved. |{' '}
              <a href="https://github.com/debdevops/servicehub/blob/main/SECURITY.md" className="text-primary-600 hover:underline">
                Security & Privacy
              </a>
            </p>
          </div>
        </div>
      </footer>
    </div>
  );
}
