// k6 load test for ServiceHub read paths against Simulator mode (no credentials, no auth).
//
//   k6 run tests/load/peek-messages.js
//   k6 run -e BASE_URL=http://localhost:8080 tests/load/peek-messages.js
//
// Asserts p95 latency and error-rate thresholds — the run FAILS if they are breached, so this
// doubles as a performance regression gate. See tests/load/README.md.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5200';
const errorRate = new Rate('servicehub_errors');

export const options = {
  scenarios: {
    ramping_reads: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: '20s', target: 10 },
        { duration: '40s', target: 25 },
        { duration: '20s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_duration: ['p(95)<800'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    servicehub_errors: ['rate<0.01'],
  },
};

// Discover seeded namespaces and their queues once, shared with all VUs.
export function setup() {
  const res = http.get(`${BASE_URL}/api/v1/simulator/status`);
  check(res, { 'simulator status 200': (r) => r.status === 200 });

  const namespaces = (res.json('namespaces') || []).filter((n) => n && n.id);
  const targets = [];

  for (const ns of namespaces) {
    const q = http.get(`${BASE_URL}/api/v1/namespaces/${ns.id}/queues`);
    if (q.status !== 200) continue;
    const queues = q.json() || [];
    for (const queue of queues) {
      const name = queue.name || queue.queueName;
      if (name) targets.push({ namespaceId: ns.id, queueName: name });
    }
  }

  if (targets.length === 0) {
    throw new Error('No simulator queues discovered — is ServiceHub running in Simulator mode?');
  }
  return { targets };
}

export default function (data) {
  const { targets } = data;
  const t = targets[Math.floor(Math.random() * targets.length)];

  const url = `${BASE_URL}/api/v1/namespaces/${t.namespaceId}/queues/${encodeURIComponent(t.queueName)}/messages?maxMessages=50`;
  const res = http.get(url);

  const ok = check(res, {
    'peek messages 200': (r) => r.status === 200,
    'peek under 800ms': (r) => r.timings.duration < 800,
  });
  errorRate.add(!ok);

  sleep(0.5);
}
