const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.join(__dirname,'../src');
const host=fs.readFileSync(path.join(root,'Prognode.Host/Services/DeviceHealthHostedService.cs'),'utf8');
const s7=fs.readFileSync(path.join(root,'Prognode.Protocols.S7/S7HealthProbe.cs'),'utf8');
const s7Pool=fs.readFileSync(path.join(root,'Prognode.Protocols.S7/S7SessionPool.cs'),'utf8');
const monitor=fs.readFileSync(path.join(root,'Prognode.Alarm/DeviceCommunicationMonitor.cs'),'utf8');
assert(host.includes('ProbeInterval = TimeSpan.FromSeconds(10)'));
assert(host.includes('Task.Delay(ProbeInterval, stoppingToken)'));
assert(host.includes('SemaphoreSlim _concurrency = new(4)'));
// The probe reuses the shared S7 session (one PLC connection) and connects with a 3 s timeout.
assert(s7.includes('pool.HasRecentSuccess') && s7.includes('pool.UseAsync'));
assert(s7Pool.includes('ConnectTimeoutMs = 3000'));
assert(monitor.includes('FailureThreshold = 3'),'single failed probe must not trigger a communication alarm');
console.log('PASS: independent health probes are paced and require three failed checks');
