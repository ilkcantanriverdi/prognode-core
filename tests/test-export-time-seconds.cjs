const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const web=fs.readFileSync(path.join(__dirname,'../src/Prognode.Web/EndpointExtensions.cs'),'utf8');
const exportBlock=web.slice(web.indexOf('"/api/historian/export.csv"'),web.indexOf('"/api/system/time"'));
assert(exportBlock.length>0);
assert(!exportBlock.includes('ToString("O"'),'export endpoints must not emit subsecond ISO timestamps');
assert(web.includes('"yyyy-MM-dd HH:mm:ss"'),'Core export formatter must stop at seconds without Z');
assert(exportBlock.includes('ExportUtcSeconds(device.CreatedAt)'));
assert(exportBlock.includes('ExportUtcSeconds(alarm.ActiveAt)'));
assert(web.includes('ExportUtcSeconds(DateTimeOffset.FromUnixTimeMilliseconds(sample.TimestampUnixMs))'));
assert(web.includes('new UTF8Encoding(true)'),'historian streaming CSV must emit a UTF-8 BOM for Excel');
assert(web.includes('var preamble = Encoding.UTF8.GetPreamble()'),'data exchange CSV must emit a UTF-8 BOM for Excel');
assert(exportBlock.includes('Math.Round(duration.TotalSeconds)'),'alarm duration must have whole seconds');

for(const file of ['trend-studio.js','trend-studio-fullscreen.js','trend-hf3plus.js']) {
  const source=fs.readFileSync(path.join(root,file),'utf8');
  assert(source.includes("toISOString().slice(0,19).replace('T',' ')"),`${file} visible CSV must stop at seconds without Z`);
  assert(/\\uFEFF/i.test(source),`${file} visible CSV must emit a UTF-8 BOM`);
}
for(const file of ['trend-studio.js','trend-studio-fullscreen.js','trend-hf3plus.js','app.js'])
  assert(!/['"`]\\u[fF][eE][fF][fF]sep=/.test(fs.readFileSync(path.join(root,file),'utf8')),`${file}: no sep= line after the BOM (Excel then ignores the BOM and breaks Turkish characters)`);
const sample=new Date('2026-09-30T12:34:56.789Z').toISOString().slice(0,19).replace('T',' ');
assert.equal(sample,'2026-09-30 12:34:56');
console.log('PASS: Core and visible Trend CSV/XLSX clock timestamps stop at seconds');
