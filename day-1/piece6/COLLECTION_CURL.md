# Duplicate quote invariant check

```bash
curl -i -X POST http://127.0.0.1:5101/api/collections/ \
  -H "Content-Type: application/json" \
  --data '{"name":"Favourites","ownerId":"user-1"}'

HTTP/1.1 201 Created
```

```bash
curl -i -X POST http://127.0.0.1:5101/api/collections/1/items/42

HTTP/1.1 200 OK
```

```bash
curl -i -X POST http://127.0.0.1:5101/api/collections/1/items/42

HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Collection invariant violated","status":400,"detail":"This quote is already in the collection."}
```
