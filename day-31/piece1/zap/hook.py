"""
zap-baseline.py hook:
1. Adds the X-Api-Key header to every outgoing request so the passive scan sees the API in its
   normal, authenticated posture instead of only ever hitting 401s.
2. Imports the OpenAPI document into ZAP's site tree, so the scan actually exercises every
   defined operation (POST /api/v1/reservations, etc.) instead of only what a plain spider can
   discover by following links - a JSON API with no HTML has almost no links to follow.
"""
import os
import time


def zap_started(zap, target):
    api_key = os.environ.get("PARKFLOW_ZAP_API_KEY", "")
    if not api_key:
        print("PARKFLOW_ZAP_API_KEY not set - skipping header replacer and OpenAPI import")
        return

    zap.replacer.add_rule(
        description="add-parkflow-api-key",
        enabled=True,
        matchtype="REQ_HEADER",
        matchregex=False,
        matchstring="X-Api-Key",
        replacement=api_key,
        initiators=None,
    )

    openapi_url = target.rstrip("/") + "/openapi/v1.json"
    result = zap.openapi.import_url(openapi_url, target)
    print(f"OpenAPI import result: {result}")
    time.sleep(2)
