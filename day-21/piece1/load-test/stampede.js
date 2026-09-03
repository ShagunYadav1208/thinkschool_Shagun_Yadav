// A burst, not sustained load: every VU fires exactly one request, as close to simultaneously
// as k6's shared-iterations executor can manage, against a key that was just evicted (so every
// one of them arrives to a genuine cache miss at the same time). Proves single-flight/stampede
// protection: with HybridCache, ICacheMetrics.DbReads should read ~1 after this, regardless of
// how many VUs were used; against /uncached, it should read ~VUS.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5299';
const TARGET_PATH = __ENV.TARGET_PATH || '/api/quotes/1';
const VUS = Number(__ENV.VUS || 50);

export const options = {
  scenarios: {
    burst: {
      executor: 'shared-iterations',
      vus: VUS,
      iterations: VUS,
      maxDuration: '15s',
    },
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function () {
  const res = http.get(`${BASE_URL}${TARGET_PATH}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
