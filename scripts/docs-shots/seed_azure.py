"""Seed the docs-orders Azure Service Bus queue with a small, realistic set: 3 healthy messages and 6 dead letters."""
import json, os, sys
from azure.servicebus import ServiceBusClient, ServiceBusMessage
cs = os.environ["AZ_SB_CS"]; Q = "docs-orders"
dead = [
  ({"orderId": "A-1001", "customerId": "C-77", "total": 42.5}, "PaymentDeclined", "Card was declined by the payment provider"),
  ({"orderId": "A-1002", "customerId": "C-12", "total": 18.0}, "PaymentDeclined", "Card was declined by the payment provider"),
  ({"orderId": "A-1003", "customerId": "C-90", "total": 99.9}, "PaymentDeclined", "Card was declined by the payment provider"),
  ({"orderId": "A-1004", "customerId": "C-31", "total": 7.25}, "InventoryUnavailable", "SKU WIDGET-9 is out of stock"),
  ({"orderId": "A-1005", "customerId": "C-44", "total": 64.0}, "InventoryUnavailable", "SKU WIDGET-9 is out of stock"),
  ({"orderId": "A-1006", "customerId": None, "total": 12.0}, "ValidationFailed", "customerId is required"),
]
with ServiceBusClient.from_connection_string(cs) as c:
    with c.get_queue_sender(Q) as s:
        for body, *_ in dead:
            s.send_messages(ServiceBusMessage(json.dumps(body), content_type="application/json", message_id=body["orderId"], subject="order.created"))
        for i, oid in enumerate(["A-2001", "A-2002", "A-2003"]):
            s.send_messages(ServiceBusMessage(json.dumps({"orderId": oid, "customerId": f"C-{i+5}", "total": 20+i}), content_type="application/json", message_id=oid, subject="order.created"))
    with c.get_queue_receiver(Q, max_wait_time=5) as r:
        want = {b["orderId"]: (reason, desc) for b, reason, desc in dead}
        for m in r.receive_messages(max_message_count=20, max_wait_time=5):
            if m.message_id in want:
                r.dead_letter_message(m, reason=want[m.message_id][0], error_description=want[m.message_id][1])
            else:
                r.abandon_message(m)
print("seeded")
