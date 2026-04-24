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
            <a
              href="https://app-servicehub-prod.azurewebsites.net/"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-8 py-3 bg-primary-600 text-white font-semibold rounded-lg hover:bg-primary-700 transition-colors shadow-lg hover:shadow-xl"
            >
              🚀 Try Free Demo (Live)
              <ArrowRight className="w-4 h-4" />
            </a>
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

          {/* Additional Capabilities */}
          <div className="mt-16 pt-16 border-t border-gray-200">
            <h3 className="text-2xl font-bold text-gray-900 text-center mb-12">
              Advanced Capabilities Built for Production
            </h3>
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              <div className="p-5 bg-gradient-to-br from-blue-50 to-white rounded-lg border border-blue-100">
                <div className="text-3xl mb-3">🔥</div>
                <h4 className="font-semibold text-gray-900 mb-2">Lightning-Fast Search</h4>
                <p className="text-sm text-gray-600">Search across millions of messages by body, headers, properties, or correlation ID in milliseconds</p>
              </div>
              <div className="p-5 bg-gradient-to-br from-purple-50 to-white rounded-lg border border-purple-100">
                <div className="text-3xl mb-3">🤖</div>
                <h4 className="font-semibold text-gray-900 mb-2">AI Pattern Detection</h4>
                <p className="text-sm text-gray-600">Automatic anomaly detection and pattern analysis to understand message failures at scale</p>
              </div>
              <div className="p-5 bg-gradient-to-br from-green-50 to-white rounded-lg border border-green-100">
                <div className="text-3xl mb-3">🎯</div>
                <h4 className="font-semibold text-gray-900 mb-2">Smart Auto-Replay</h4>
                <p className="text-sm text-gray-600">Create intelligent replay rules with conditional logic, scheduling, and automatic success/failure handling</p>
              </div>
              <div className="p-5 bg-gradient-to-br from-orange-50 to-white rounded-lg border border-orange-100">
                <div className="text-3xl mb-3">📈</div>
                <h4 className="font-semibold text-gray-900 mb-2">Real-Time Analytics</h4>
                <p className="text-sm text-gray-600">Live dashboards with queue metrics, message trends, failure rates, and performance insights</p>
              </div>
              <div className="p-5 bg-gradient-to-br from-pink-50 to-white rounded-lg border border-pink-100">
                <div className="text-3xl mb-3">🔗</div>
                <h4 className="font-semibold text-gray-900 mb-2">Correlation Explorer</h4>
                <p className="text-sm text-gray-600">Trace message flow across topics, queues, and subscriptions with built-in correlation tracking</p>
              </div>
              <div className="p-5 bg-gradient-to-br from-cyan-50 to-white rounded-lg border border-cyan-100">
                <div className="text-3xl mb-3">📅</div>
                <h4 className="font-semibold text-gray-900 mb-2">Scheduled Messages</h4>
                <p className="text-sm text-gray-600">View, manage, and test scheduled messages with timing validation and retry scheduling</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Azure Portal Comparison */}
      <section className="py-20 px-6 bg-gradient-to-r from-red-50/30 to-blue-50/30">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-12">
            ServiceHub vs Azure Portal: Feature Comparison
          </h2>

          <div className="overflow-x-auto rounded-lg border border-gray-200">
            <table className="w-full">
              <thead>
                <tr className="bg-gray-100 border-b border-gray-200">
                  <th className="px-6 py-4 text-left font-semibold text-gray-900">Capability</th>
                  <th className="px-6 py-4 text-center font-semibold text-gray-600">Azure Portal</th>
                  <th className="px-6 py-4 text-center font-semibold text-primary-600">ServiceHub</th>
                </tr>
              </thead>
              <tbody>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">View Message Body & Content</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Count Only</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Full Body + Syntax</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Search Across Message Content</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Real-Time Full-Text</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">DLQ Investigation</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ One at a Time</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Batch Analysis</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">AI Pattern Detection</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Client-Side Clustering</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Replay from DLQ</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ One-Click or Auto-Rules</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">30-Day DLQ Trends</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Full History + Charts</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Correlation ID Tracing</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Cross-Queue Journeys</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Multi-Namespace Support</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Portal Only</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Simultaneous Management</td>
                </tr>
                <tr className="border-b border-gray-100 hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Scheduled Message Management</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ View, Reschedule, Cancel</td>
                </tr>
                <tr className="hover:bg-white/50">
                  <td className="px-6 py-3 text-gray-900">Auto-Replay Rules Engine</td>
                  <td className="px-6 py-3 text-center text-red-600">❌ Not Available</td>
                  <td className="px-6 py-3 text-center text-green-600">✅ Smart Matching + Safety</td>
                </tr>
              </tbody>
            </table>
          </div>

          <div className="mt-8 p-6 bg-blue-50 border border-blue-200 rounded-lg">
            <p className="text-center text-gray-700 font-semibold">
              🎯 Bottom line: <span className="text-primary-600">ServiceHub does in minutes what takes hours in the Portal.</span>
            </p>
          </div>
        </div>
      </section>

      {/* Lucrative Use Cases */}
      <section className="py-20 px-6">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-3xl font-bold text-gray-900 text-center mb-12">
            Real-World Scenarios Where ServiceHub Shines
          </h2>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
            <div className="p-6 bg-gradient-to-br from-amber-50 to-white rounded-xl border border-amber-200">
              <div className="text-4xl mb-3">🚨</div>
              <h3 className="text-lg font-bold text-gray-900 mb-3">Production Incident @ 2 AM</h3>
              <p className="text-gray-600 mb-3">10,000 messages pile up in DLQ. Azure Portal shows counts but no visibility into why. With ServiceHub, you search all messages in 10 seconds, find the pattern, and auto-replay in minutes instead of hours.</p>
              <p className="text-xs text-amber-700 font-semibold">⏱️ Time saved: ~4 hours</p>
            </div>

            <div className="p-6 bg-gradient-to-br from-purple-50 to-white rounded-xl border border-purple-200">
              <div className="text-4xl mb-3">🔍</div>
              <h3 className="text-lg font-bold text-gray-900 mb-3">Post-Mortem Analysis</h3>
              <p className="text-gray-600 mb-3">DLQ Intelligence stores 30 days of failure history. Graph trends over time, categorize failures (Transient, MaxDelivery, Expired), and extract patterns for prevention. Export CSV for stakeholder reports.</p>
              <p className="text-xs text-purple-700 font-semibold">📊 Data-driven insights</p>
            </div>

            <div className="p-6 bg-gradient-to-br from-green-50 to-white rounded-xl border border-green-200">
              <div className="text-4xl mb-3">🎯</div>
              <h3 className="text-lg font-bold text-gray-900 mb-3">Automated Recovery</h3>
              <p className="text-gray-600 mb-3">Deploy auto-replay rules that watch for transient failures and replay them automatically. ServiceHub templates cover throttling errors, timeouts, and TTL expiry — set it and forget it.</p>
              <p className="text-xs text-green-700 font-semibold">⚡ Zero manual intervention</p>
            </div>

            <div className="p-6 bg-gradient-to-br from-blue-50 to-white rounded-xl border border-blue-200">
              <div className="text-4xl mb-3">🔗</div>
              <h3 className="text-lg font-bold text-gray-900 mb-3">Distributed Debugging</h3>
              <p className="text-gray-600 mb-3">Trace a Correlation ID across all queues, topics, and subscriptions to see where a message ended up. Debug multi-hop workflows and find where processing broke.</p>
              <p className="text-xs text-blue-700 font-semibold">🕵️ End-to-end visibility</p>
            </div>
          </div>
        </div>
      </section>

      {/* Trust & Enterprise */}
      <section className="py-16 px-6 bg-gradient-to-r from-primary-50 to-blue-50">
        <div className="max-w-4xl mx-auto text-center">
          <h2 className="text-2xl font-bold text-gray-900 mb-8">Enterprise-Grade, But Developer-Friendly</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div className="p-5 bg-white rounded-lg border border-gray-200">
              <div className="text-2xl mb-2">🔐</div>
              <h4 className="font-semibold text-gray-900 mb-2">Zero-Trust Privacy</h4>
              <p className="text-sm text-gray-600">All AI analysis runs client-side. No message content leaves your browser. AES-GCM encryption for connection strings at rest.</p>
            </div>
            <div className="p-5 bg-white rounded-lg border border-gray-200">
              <div className="text-2xl mb-2">⚙️</div>
              <h4 className="font-semibold text-gray-900 mb-2">One-Command Setup</h4>
              <p className="text-sm text-gray-600">Clone the repo, run <span className="font-mono text-xs bg-gray-100 px-2 py-1">./run.sh</span>, paste your connection string. Live in 30 seconds.</p>
            </div>
            <div className="p-5 bg-white rounded-lg border border-gray-200">
              <div className="text-2xl mb-2">📦</div>
              <h4 className="font-semibold text-gray-900 mb-2">Self-Hosted Control</h4>
              <p className="text-sm text-gray-600">Deploy to your own infrastructure (Docker, Azure App Service, Kubernetes) or use our free hosted demo. You decide.</p>
            </div>
          </div>
        </div>
      </section>
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
            No credit card needed. No setup required. Try the demo right now — connect any Azure Service Bus in 30 seconds.
          </p>
          <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
            <a
              href="https://app-servicehub-prod.azurewebsites.net/"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-8 py-4 bg-white text-primary-600 font-bold rounded-lg hover:bg-gray-100 transition-colors shadow-lg"
            >
              🚀 Launch Free Demo
              <ArrowRight className="w-5 h-5" />
            </a>
            <Link
              to="/app/connect"
              className="inline-flex items-center gap-2 px-8 py-4 bg-white/20 text-white font-bold rounded-lg border-2 border-white hover:bg-white/30 transition-colors"
            >
              💻 Self-Host on localhost
              <ArrowRight className="w-5 h-5" />
            </Link>
          </div>
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
                  <Link to="/app/security" className="hover:text-primary-600">
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
