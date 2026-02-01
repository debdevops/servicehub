#!/bin/zsh

CONNECTION_STRING="${SERVICEHUB_CONNECTION_STRING:-}"

if [ -z "$CONNECTION_STRING" ]; then
  echo "Missing SERVICEHUB_CONNECTION_STRING env var."
  exit 1
fi

for port in 3000 5153; do
  pid=$(lsof -nP -iTCP:$port -sTCP:LISTEN -t || true)
  if [ -n "$pid" ]; then
    kill -9 $pid
  fi
done

nohup dotnet run --project /Users/debasisghosh/Github/servicehub/services/api/src/ServiceHub.Api/ServiceHub.Api.csproj > /tmp/servicehub_api.log 2>&1 &

for i in {1..30}; do
  if curl -s http://localhost:5153/api/health/status > /dev/null; then
    break
  fi
  sleep 1
done

create_status=$(curl -s -o /tmp/namespace_create.json -w "%{http_code}" -X POST http://localhost:5153/api/v1/namespaces -H "Content-Type: application/json" -d "{\"name\":\"sb-inspector-local-dg\",\"connectionString\":\"${CONNECTION_STRING}\",\"authType\":0,\"displayName\":\"DevEnvironment\",\"description\":\"Local dev namespace\"}")

if [ "$create_status" = "201" ]; then
  NS_ID=$(python -c "import json;print(json.load(open('/tmp/namespace_create.json'))['id'])")
else
  NS_ID=$(curl -s http://localhost:5153/api/v1/namespaces | python -c "import json,sys;items=json.load(sys.stdin);print(items[0]['id'] if items else '')")
fi

curl -s http://localhost:5153/api/v1/namespaces/$NS_ID/queues > /tmp/queues.json
curl -s http://localhost:5153/api/v1/namespaces/$NS_ID/topics > /tmp/topics.json

QUEUE_NAME=$(python -c "import json;queues=json.load(open('/tmp/queues.json'));print(queues[0]['name'] if queues else '')")
TOPIC_NAME=$(python -c "import json;topics=json.load(open('/tmp/topics.json'));print(topics[0]['name'] if topics else '')")

QUEUE_STATUS=$(curl -s -o /tmp/send_queue_response.txt -w "%{http_code}" -X POST http://localhost:5153/api/v1/namespaces/$NS_ID/queues/$QUEUE_NAME/messages -H "Content-Type: application/json" -d '{"body":"{\"hello\":\"world\"}","contentType":"application/json","applicationProperties":{"ServiceHub-Generated":"true","priority":"high"},"correlationId":"test-corr"}')

if [ -n "$TOPIC_NAME" ]; then
  TOPIC_STATUS=$(curl -s -o /tmp/send_topic_response.txt -w "%{http_code}" -X POST http://localhost:5153/api/v1/namespaces/$NS_ID/topics/$TOPIC_NAME/messages -H "Content-Type: application/json" -d '{"body":"{\"hello\":\"topic\"}","contentType":"application/json","applicationProperties":{"ServiceHub-Generated":"true","priority":"high"},"correlationId":"test-corr-topic"}')
else
  TOPIC_STATUS="no-topics"
fi

printf "NamespaceId=%s\nQueue=%s Status=%s\nTopic=%s Status=%s\n" "$NS_ID" "$QUEUE_NAME" "$QUEUE_STATUS" "$TOPIC_NAME" "$TOPIC_STATUS"

nohup npm --prefix /Users/debasisghosh/Github/servicehub/apps/web run dev -- --port 3000 > /tmp/servicehub_ui.log 2>&1 &
