# PROGNODE protocol soak · 2026-10-14 09:05 · 30 min

Simulated devices; one taken down every 2 min for 20 s. Poll interval 1 s.

| Protocol | Tags | Reads | Good outside outages | Wrong values | Failed reads | Latency p50 / p95 / max (ms) | Outages | Detect (s) avg / max | Recover (s) avg / max | Result |
|---|---:|---:|---:|---:|---:|---|---:|---|---|---|
| Modbus TCP | 308 | 1734 | 100,000% | 0 | 29 | 2 / 4 / 2033 | 3 | 0,5 / 1,0 | 0,4 / 0,6 | PASS |
| Siemens S7 | 208 | 1735 | 100,000% | 0 | 30 | 1 / 1 / 2052 | 3 | 0,4 / 0,5 | 0,7 / 0,8 | PASS |
| OPC UA | 206 | 1743 | 100,000% | 0 | 20 | 4 / 6 / 2070 | 2 | 0,4 / 0,7 | 1,7 / 1,7 | PASS |
| MQTT | 53 | 1745 | 100,000% | 0 | 18 | 0 / 0 / 2060 | 4 | 5,4 / 10,3 | 1,9 / 2,4 | PASS |

Memory (private): 29 MB after warm-up → 30 MB at the end (max 34 MB). Managed heap 7 MB. Threads 31 → 26, handles 601 → 654.

## Modbus TCP
- 29× IOException: Modbus TCP timeout after 2000 ms.

## Siemens S7
- 27× SocketException: No connection could be made because the target machine actively refused it.
- 3× IOException: Unable to write data to the transport connection: An existing connection was forcibly closed by the remote 

## OPC UA
- 18× SocketException: No connection could be made because the target machine actively refused it.
- 2× ServiceResultException: [80AE0000] (BadConnectionClosed)

## MQTT
- 18× MqttCommunicationException: Error while connecting host 'Unspecified/127.0.0.1:11883'.

PASS means: no wrong value, at least 99.9 % Good readings outside outages, every outage recovered within 60 s, and (runs of 30 min or more) less than 100 MB memory growth.
