// Sustained load against one hot key, for a before/after DB-queries/sec and p99 comparison.
// Point PATH at /api/quotes/{id} (cached) or /api/quotes/{id}/uncached (baseline) via env var.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5299';
const TARGET_PATH = __ENV.TARGET_PATH || '/api/quotes/1';

export const options = {
  scenarios: {
    sustained: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 50),
      duration: __ENV.DURATION || '20s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function () {
  const res = http.get(`${BASE_URL}${TARGET_PATH}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
