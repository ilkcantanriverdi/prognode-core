const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
const slice=(from,to)=>{const s=source.indexOf(from),e=source.indexOf(to,s);assert(s>=0&&e>s,from);return source.slice(s,e);};
const context={Number,String,Math};
vm.createContext(context);
vm.runInContext(slice('function modbusAddressInfo(','function nextModbusTagAddress('),context);
vm.runInContext(slice('const BACNET_TYPES','// MQTT Tag address').replace('const BACNET_TYPES','var BACNET_TYPES'),context);
vm.runInContext(slice('function bulkTagName(','function tagFormBody('),context);
const at=(a,type,bit,protocol,n,series)=>context.bulkTagAddress(a,type,bit,protocol,n,series);
assert.equal(context.bulkTagName('Temp',0),'Temp1');
assert.equal(context.bulkTagName('Temp',19),'Temp20');
assert.equal(context.bulkTagName('Pump07',2),'Pump09','trailing number continues, width kept');
assert.deepEqual({...at('40001','Float32',null,'Modbus TCP',19)},{address:'40039',bitIndex:null},'Float32 takes two registers');
assert.deepEqual({...at('40001','UInt16',null,'Modbus TCP',3)},{address:'40004',bitIndex:null});
assert.deepEqual({...at('00001','Bool',null,'Modbus TCP',4)},{address:'00005',bitIndex:null},'coils step by one');
assert.deepEqual({...at('40010','Bool',14,'Modbus TCP',3)},{address:'40011',bitIndex:1},'register bits roll over to the next register');
assert.equal(at('49999','Float32',null,'Modbus TCP',0),null,'32-bit value cannot start on the last register');
assert.equal(at('49998','UInt16',null,'Modbus TCP',2),null,'past the end of the area');
assert.deepEqual({...at('DB1.DBD0','Float32',null,'Siemens S7 TCP',2)},{address:'DB1.DBD8',bitIndex:null});
assert.deepEqual({...at('DB3.DBW10','Int16',null,'Siemens S7 TCP',1)},{address:'DB3.DBW12',bitIndex:null});
assert.deepEqual({...at('DB1.DBX0.6','Bool',null,'Siemens S7 TCP',3)},{address:'DB1.DBX1.1',bitIndex:null},'S7 bits roll over to the next byte');
assert.deepEqual({...at('ns=2;i=5','Float32',null,'OPC UA',2)},{address:'ns=2;i=7',bitIndex:null});
assert.deepEqual({...at('line/sensor01','Float32',null,'MQTT',9)},{address:'line/sensor10',bitIndex:null});
assert.equal(at('ns=2;s=Temperature','Float32',null,'OPC UA',1),null,'no trailing number: cannot continue');
// S7 inputs, outputs and bit memory.
assert.deepEqual({...at('MD4','Float32',null,'Siemens S7 TCP',2)},{address:'MD12',bitIndex:null},'MD steps by four bytes');
assert.deepEqual({...at('%IW64','Int16',null,'Siemens S7 TCP',1)},{address:'%IW66',bitIndex:null});
assert.deepEqual({...at('I0.6','Bool',null,'Siemens S7 TCP',3)},{address:'I1.1',bitIndex:null},'input bits roll over to the next byte');
// Mitsubishi: words by width, hex X/Y on Q/L/iQ-R, octal on FX5, hex bits of a word.
assert.deepEqual({...at('D100','Float32',null,'Mitsubishi MC',2,0)},{address:'D104',bitIndex:null},'Float32 takes two words');
assert.deepEqual({...at('X1F','Bool',null,'Mitsubishi MC',1,0)},{address:'X20',bitIndex:null},'hex X');
assert.deepEqual({...at('X17','Bool',null,'Mitsubishi MC',1,1)},{address:'X20',bitIndex:null},'octal X on FX5');
assert.deepEqual({...at('D100.E','Bool',null,'Mitsubishi MC',3,0)},{address:'D101.1',bitIndex:null},'word bits roll over after F');
assert.deepEqual({...at('SD10','Word',null,'Mitsubishi MC',1,0)},{address:'SD11',bitIndex:null});
// Omron: words by width, decimal bits 00..15, timers/counters by one.
assert.deepEqual({...at('D100','Float32',null,'Omron FINS',2)},{address:'D104',bitIndex:null},'Float32 takes two words');
assert.deepEqual({...at('CIO0.14','Bool',null,'Omron FINS',3)},{address:'CIO1.01',bitIndex:null},'bits roll over after 15');
assert.deepEqual({...at('E0_100','Int64',null,'Omron FINS',1)},{address:'E0_104',bitIndex:null});
assert.deepEqual({...at('T5','Bool',null,'Omron FINS',2)},{address:'T7',bitIndex:null});
// Logix: array elements and bits of an integer tag.
assert.deepEqual({...at('Counters[3]','Int16',null,'EtherNet/IP',2)},{address:'Counters[5]',bitIndex:null});
assert.deepEqual({...at('Grid[1,9]','Word',null,'EtherNet/IP',1)},{address:'Grid[1,10]',bitIndex:null});
assert.deepEqual({...at('Status.30','Bool',null,'EtherNet/IP',1)},{address:'Status.31',bitIndex:null});
assert.deepEqual({...at('Motor1','Float32',null,'EtherNet/IP',2)},{address:'Motor3',bitIndex:null},'numbered tag names');
assert.equal(at('Status.63','Bool',null,'EtherNet/IP',1),null,'no bit past 63');
// BACnet: next instance, same written form.
assert.deepEqual({...at('AV:1','Float32',null,'BACnet/IP',4)},{address:'AV:5',bitIndex:null});
assert.deepEqual({...at('analog-input,9','Float32',null,'BACnet/IP',1)},{address:'analog-input,10',bitIndex:null});
assert.deepEqual({...at('BV 3','Bool',null,'BACnet/IP',2)},{address:'BV 5',bitIndex:null});
assert.equal(at('AI:4194302','Float32',null,'BACnet/IP',1),null,'past the last instance');
console.log('PASS: bulk tag add numbers names and continues Modbus, S7, Mitsubishi, Omron, Logix, BACnet and numbered addresses');
