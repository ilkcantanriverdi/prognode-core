const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.join(__dirname,
  '../src/Prognode.Web/EndpointExtensions.cs'), 'utf8');
for (const [route, module] of [
  ['/api/alarms/definitions/{id:guid}', 'ALARM'],
  ['/api/historian/configurations/{id:guid}', 'HISTORIAN']
]) {
  const marker = `"${route}"`;
  const start = source.indexOf(marker);
  assert(start >= 0, `${route} edit route exists`);
  const end = source.indexOf('endpoints.MapDelete(', start);
  assert(end > start, `${route} edit route is followed by delete`);
  const edit = source.slice(start, end);
  assert(edit.includes('LicenseService license'), `${route} obtains license policy`);
  assert(edit.includes(`!license.HasModule("${module}")`),
    `${route} refuses updates without ${module} entitlement`);
  assert(edit.includes('StatusCodes.Status403Forbidden'),
    `${route} returns forbidden when entitlement is absent`);
}
console.log('PASS: Alarm and Historian edit routes enforce their license modules');
