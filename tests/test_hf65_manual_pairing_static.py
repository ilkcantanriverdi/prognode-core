from pathlib import Path
import hashlib,json,re
root=Path(__file__).resolve().parents[1];v=json.loads((root/'tests/ManualPairingContract/golden-vector.json').read_text())
b=(root/v['certDerFixture']).read_bytes();h=hashlib.sha256(b).digest();assert h.hex().upper()==v['certSha256Hex']
server=v['serverId'];full=hashlib.sha256(b'PROGNODE-MANUAL-PAIR-V1\x00'+server.encode()+b'\x00'+h).digest()
x=int.from_bytes(full[:8],'big')>>4;alphabet=v['sasAlphabet'];digits=''.join(alphabet[(x>>(5*(11-i)))&31] for i in range(12));control='-'.join(digits[i:i+4] for i in (0,4,8))
assert control==v['controlCode']
endpoint=(root/'src/Prognode.Web/EndpointExtensions.cs').read_text();assert 'CheckQrAdmin(context, sessions)' in endpoint and 'manual-pairing' in endpoint and 'CheckHttpsAsync' in endpoint
identity=endpoint.split('"/api/server/identity"',1)[1].split('endpoints.MapGet(',1)[0];assert 'PairingCode' not in identity
html=(root/'src/Prognode.Host/wwwroot/index.html').read_text();assert all(x in html for x in ('pairManualPanel','pairQrPanel','manualSasCode'))
print('PASS independent golden vector: '+control)
print('PASS static admin + TLS gating / public identity exclusions + UI wiring')
