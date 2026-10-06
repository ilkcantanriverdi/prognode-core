# PROGNODE S25 Mobile Notification MVP

First test should be LAN-local, before FCM/cloud relay.

Flow:
1. Android discovers/pairs to Core using the same ServerId + UDP 5081 + pairing code contract as Windows Client.
2. App polls `GET /api/notifications?after=<lastId>` with the paired Bearer token while on the same LAN.
3. Android posts notifications through two channels: Normal and Critical.
4. Test event can be generated from Core with existing `POST /api/notifications/test`.
5. After LAN notification delivery is proven on the Galaxy S25, add FCM + optional PROGNODE Cloud relay for remote delivery.

Why this order: it proves Core -> event -> paired mobile client -> Android notification independently of cloud infrastructure. Process data remains local; only notification metadata needs relay when remote push is added.
