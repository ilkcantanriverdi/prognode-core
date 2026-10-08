const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../src/Prognode.Host/wwwroot/app.js'), 'utf8');
const start = source.indexOf('function tagValueText(');
const end = source.indexOf('function renderTags(', start);
assert(start >= 0 && end > start);
const context = {state:{language:'en'}};
vm.createContext(context);
vm.runInContext(source.slice(start,end),context);

assert.equal(context.tagValueText({dataType:'Word',decimalPlaces:3},{value:12345}),'12345');
assert.equal(context.tagValueText({dataType:'Word',decimalPlaces:0},{value:65535}),'65535');
assert.equal(context.tagValueText({dataType:'Word'},{value:0}),'0');
// Measurements are formatted for the English UI; WORD stays a raw bitfield either way.
assert.equal(context.tagValueText({dataType:'UInt16',decimalPlaces:2},{value:1234.5}),'1,234.50');
assert.equal(context.tagValueText({dataType:'Word'},{value:12345}),'12345');
console.log('PASS: WORD Tag values have no locale group/decimal separator');
