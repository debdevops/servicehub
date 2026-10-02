#!/usr/bin/env bash
# Rebuild the Azure screenshot set from scratch: reseed the real docs-orders queue, restart the throw-away instance on an empty DB, capture.
set -euo pipefail
cd "$(dirname "$0")"
export AZ_SB_CS="$(az servicebus namespace authorization-rule keys list -g rg-servicehub-dev --namespace-name sb-servicehub-dev -n servicehub-app --query primaryConnectionString -o tsv)"
python3 - <<'PY'
import os
from azure.servicebus import ServiceBusClient, ServiceBusSubQueue
with ServiceBusClient.from_connection_string(os.environ["AZ_SB_CS"]) as c:
    for sq in (None, ServiceBusSubQueue.DEAD_LETTER):
        kw = {"sub_queue": sq} if sq else {}
        with c.get_queue_receiver("docs-orders", **kw) as r:
            while True:
                ms = r.receive_messages(max_message_count=100, max_wait_time=3)
                if not ms: break
                for m in ms: r.complete_message(m)
PY
python3 seed_azure.py
./reset-instance.sh
node azure.spec.mjs
