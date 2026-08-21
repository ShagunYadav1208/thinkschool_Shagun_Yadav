import http from 'k6/http';
import { check } from 'k6';

export const options = {
    scenarios: {
        constant_load: {
            executor: 'constant-vus',
            vus: 10,
            duration: '30s',
        },
    },
    thresholds: {
        http_req_failed: ['rate==0'],
    },
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(90)', 'p(95)', 'p(99)'],
};

const BASE_URL = __ENV.TARGET_URL || 'http://localhost:5299/authors-summary-slow';

export default function () {
    const res = http.get(BASE_URL);
    check(res, { 'status is 200': (r) => r.status === 200 });
}
