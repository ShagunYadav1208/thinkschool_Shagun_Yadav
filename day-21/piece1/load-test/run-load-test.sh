#!/usr/bin/env bash
# Day 21 load test: runs the same request shape against the cached hot read
# (GET /api/quotes/{id}) and the uncached comparison endpoint
# (GET /api/quotes/{id}/uncached), for a real before/after DB-queries/sec and
# p99 comparison, then proves stampede protection with a concurrent burst
# against a freshly-evicted key.
#
# Requires: the API running (see README), k6, node, curl. Run from this
# directory: ./run-load-test.sh
set -euo pipefail
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL="*"

BASE_URL="${BASE_URL:-http://localhost:5299}"
QUOTE_ID="${QUOTE_ID:-1}"
SUSTAINED_VUS="${SUSTAINED_VUS:-50}"
SUSTAINED_DURATION="${SUSTAINED_DURATION:-20s}"
STAMPEDE_VUS="${STAMPEDE_VUS:-50}"

# k6 and node are native Windows binaries and can't resolve Git Bash's /c/... POSIX-style
# paths - `pwd -W` (a Git Bash builtin) gives the forward-slashed Windows-style path
# (e.g. C:/Users/...) that both bash and native Windows exes accept.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -W)"
TMP_DIR="$(mktemp -d)"
TMP_DIR="$(cd "$TMP_DIR" && pwd -W)"

metrics() { curl -s "$BASE_URL/api/cache/metrics"; }
db_reads() { node -e "console.log(JSON.parse(require('fs').readFileSync(0,'utf8')).dbReads)"; }
reset_metrics() { curl -s -X POST "$BASE_URL/api/cache/metrics/reset" > /dev/null; }
evict() { curl -s -X POST "$BASE_URL/api/cache/evict/$QUOTE_ID" > /dev/null; }

p99_of() {
  node -e "
    const s = JSON.parse(require('fs').readFileSync('$1', 'utf8'));
    console.log(s.metrics.http_req_duration['p(99)'].toFixed(2));
  "
}
reqs_per_sec_of() {
  node -e "
    const s = JSON.parse(require('fs').readFileSync('$1', 'utf8'));
    console.log(s.metrics.http_reqs.rate.toFixed(2));
  "
}

echo "== Day 21: HybridCache load test =="
echo "Base URL: $BASE_URL   Quote id: $QUOTE_ID"
echo

echo "--- 1) BASELINE (uncached): $SUSTAINED_VUS VUs for $SUSTAINED_DURATION ---"
reset_metrics
evict
BEFORE=$(metrics | db_reads)
BASE_URL="$BASE_URL" TARGET_PATH="/api/quotes/$QUOTE_ID/uncached" VUS="$SUSTAINED_VUS" DURATION="$SUSTAINED_DURATION" \
  k6 run --quiet --summary-export="$TMP_DIR/uncached.json" "$SCRIPT_DIR/hot-read.js" > "$TMP_DIR/uncached.log" 2>&1 || true
AFTER=$(metrics | db_reads)
UNCACHED_DB_READS=$((AFTER - BEFORE))
UNCACHED_P99=$(p99_of "$TMP_DIR/uncached.json")
UNCACHED_RPS=$(reqs_per_sec_of "$TMP_DIR/uncached.json")
echo "DB reads: $UNCACHED_DB_READS   HTTP req/s: $UNCACHED_RPS   p99: ${UNCACHED_P99}ms"
echo

echo "--- 2) CACHED (HybridCache): $SUSTAINED_VUS VUs for $SUSTAINED_DURATION ---"
reset_metrics
evict
curl -s "$BASE_URL/api/quotes/$QUOTE_ID" > /dev/null   # warm the cache once, outside the timed run
BEFORE=$(metrics | db_reads)
BASE_URL="$BASE_URL" TARGET_PATH="/api/quotes/$QUOTE_ID" VUS="$SUSTAINED_VUS" DURATION="$SUSTAINED_DURATION" \
  k6 run --quiet --summary-export="$TMP_DIR/cached.json" "$SCRIPT_DIR/hot-read.js" > "$TMP_DIR/cached.log" 2>&1 || true
AFTER=$(metrics | db_reads)
CACHED_DB_READS=$((AFTER - BEFORE))
CACHED_P99=$(p99_of "$TMP_DIR/cached.json")
CACHED_RPS=$(reqs_per_sec_of "$TMP_DIR/cached.json")
echo "DB reads: $CACHED_DB_READS   HTTP req/s: $CACHED_RPS   p99: ${CACHED_P99}ms"
echo

echo "--- 3) STAMPEDE PROTECTION: $STAMPEDE_VUS concurrent requests at a cold key ---"
echo "  a) cached endpoint (single-flight expected)"
reset_metrics
evict
BASE_URL="$BASE_URL" TARGET_PATH="/api/quotes/$QUOTE_ID" VUS="$STAMPEDE_VUS" \
  k6 run --quiet "$SCRIPT_DIR/stampede.js" > "$TMP_DIR/stampede-cached.log" 2>&1 || true
STAMPEDE_CACHED=$(metrics | db_reads)
echo "     $STAMPEDE_VUS concurrent requests -> $STAMPEDE_CACHED DB read(s)"

echo "  b) uncached endpoint (no protection, expect ~$STAMPEDE_VUS)"
reset_metrics
evict
BASE_URL="$BASE_URL" TARGET_PATH="/api/quotes/$QUOTE_ID/uncached" VUS="$STAMPEDE_VUS" \
  k6 run --quiet "$SCRIPT_DIR/stampede.js" > "$TMP_DIR/stampede-uncached.log" 2>&1 || true
STAMPEDE_UNCACHED=$(metrics | db_reads)
echo "     $STAMPEDE_VUS concurrent requests -> $STAMPEDE_UNCACHED DB read(s)"
echo

echo "== Summary =="
printf "%-28s %12s %12s\n" "" "Uncached" "Cached"
printf "%-28s %12s %12s\n" "DB reads (sustained run)" "$UNCACHED_DB_READS" "$CACHED_DB_READS"
printf "%-28s %12s %12s\n" "HTTP req/s" "$UNCACHED_RPS" "$CACHED_RPS"
printf "%-28s %12s %12s\n" "p99 latency (ms)" "$UNCACHED_P99" "$CACHED_P99"
printf "%-28s %12s %12s\n" "DB reads under $STAMPEDE_VUS-way stampede" "$STAMPEDE_UNCACHED" "$STAMPEDE_CACHED"
echo
echo "Raw k6 logs and JSON summaries kept in: $TMP_DIR"
