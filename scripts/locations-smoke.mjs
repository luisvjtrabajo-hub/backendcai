// Run against the disposable local database/API only. API must use Locations__BaseUrl=http://127.0.0.1:58081/.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const base = process.env.TEST_API_URL || 'http://localhost:58080';
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname) || !process.env.TEST_PSQL) throw Error('Only local test API and TEST_PSQL are allowed.');
const sql = query => execFileSync(process.env.TEST_PSQL, ['-h','127.0.0.1','-p','55432','-U','cai_test','-d','cai_test','-X','-v','ON_ERROR_STOP=1','-Atq'], {input:query,encoding:'utf8'}).trim();
const uuid = randomUUID();
const password = `Local-${uuid}!`;
let checks = 0;
const check = (actual, expected) => { assert.deepEqual(actual, expected); checks++; };
async function call(action, data = {}, token, status = 200) {
  const response = await fetch(`${base}/api`, {method:'POST',headers:{'Content-Type':'application/json',...(token ? {Authorization:`Bearer ${token}`} : {})},body:JSON.stringify({action,data})});
  const result = await response.json();
  check([response.status, response.status === status ? null : result], [status, null]);
  return result;
}
let outage = false;
const provider = createServer((req, res) => {
  if (outage) { res.writeHead(503).end(); return; }
  const query = new URL(req.url, 'http://localhost');
  assert.equal(query.searchParams.get('countryCode'), 'CL');
  res.setHeader('Content-Type', 'application/json');
  res.end(JSON.stringify({results:[{id:3871336,name:'Santiago',country:'Chile',country_code:'CL',admin1:'Región Metropolitana',feature_code:'PPLC',latitude:-33.45694,longitude:-70.64827},{id:9999999,name:'Mountain',country:'Chile',country_code:'CL',feature_code:'MT',latitude:1,longitude:1},{id:3448439,name:'Santiago',country:'Brasil',country_code:'BR',feature_code:'PPL',latitude:-29.19,longitude:-54.86}]}));
});
await new Promise(resolve => provider.listen(58081, '127.0.0.1', resolve));
try {
  const admin = await call('auth.login', {email:process.env.TEST_ADMIN_EMAIL,password:process.env.TEST_ADMIN_PASSWORD});
  const token = admin.accessToken;
  await call('members.map', {}, undefined, 401);
  await call('locations.search', {q:'S',countryCode:'CL'}, undefined, 400);
  await call('locations.search', {q:'Santiago',countryCode:'C1'}, undefined, 400);
  const cities = await call('locations.search', {q:'Santiago',countryCode:'CL'});
  check(cities.items.length, 1);
  const locationId = cities.items[0].id;
  check(cities.items[0].country, 'Chile');
  check((await call('locations.search', {q:'Santiago',countryCode:'CL'})).items[0].id, locationId);
  const registration = {email:`location-${uuid}@example.com`,password,fullName:`Location test ${uuid}`};
  await call('auth.register', registration, undefined, 400);
  await call('auth.register', {...registration,locationId:randomUUID()}, undefined, 400);
  await call('auth.login', {email:registration.email,password}, undefined, 401);
  const user = await call('auth.register', {...registration,locationId,city:'Fake',country:'Fake',latitude:0});
  check([user.user.city,user.user.country,user.user.locationId], ['Santiago','Chile',locationId]);
  await call('members.map', {}, user.accessToken, 403);
  const before = (await call('members.map', {country:'Chile',city:'Santiago'}, token)).items.find(l=>l.id===locationId)?.memberCount || 0;
  await call('users.activate', {id:user.user.id}, token);
  await call('members.map', {}, user.accessToken, 403);
  await call('overview.get', {}, user.accessToken, 403);
  const point = (await call('members.map', {country:'chile',city:'santi'}, token)).items.find(l=>l.id===locationId);
  check([point.memberCount,point.latitude,point.longitude], [before+1,-33.45694,-70.64827]);
  check(Object.hasOwn(point,'email'), false);
  check((await call('users.list', {country:'chile',city:'santi',q:uuid}, token)).items.map(u=>u.id), [user.user.id]);
  check((await call('members.list', {country:'chile',city:'santi',q:uuid}, user.accessToken)).items.map(u=>u.id), [user.user.id]);
  check((await call('members.list', {country:'Perú',q:uuid}, user.accessToken)).total, 0);
  await call('profile.update', {birthDate:'1990-01-01',locationId:randomUUID()}, user.accessToken, 400);
  check((await call('progress.get', {}, user.accessToken)).birthDate, null);
  await call('profile.update', {birthDate:'1990-01-01',city:'Fake'}, user.accessToken, 400);
  const updated = await call('profile.update', {birthDate:'1990-01-01',locationId}, user.accessToken);
  check([updated.city,updated.country,updated.countryCode,updated.totalPoints], ['Santiago','Chile','CL',0]);
  // A real relocation removes the old marker and moves the member to the new city.
  const other = randomUUID();
  sql(`INSERT INTO city_locations(id,provider_id,country,country_code,city,latitude,longitude) VALUES('${other}',-1,'Perú','PE','Lima',-12.0464,-77.0428) ON CONFLICT(provider_id) DO UPDATE SET id=city_locations.id;`);
  const otherId = sql('SELECT id FROM city_locations WHERE provider_id=-1;');
  await call('profile.update', {birthDate:'1990-01-01',locationId:otherId}, user.accessToken);
  check((await call('users.list', {country:'Chile',q:uuid}, token)).total, 0);
  check((await call('members.map', {country:'Perú',city:'Lima'}, token)).items.find(l=>l.id===otherId).memberCount >= 1, true);
  await call('users.deactivate', {id:user.user.id}, token);
  check((await call('users.list', {country:'Perú',role:'SOLDADO_ACTIVE',q:uuid}, token)).total, 0);
  outage = true;
  await call('locations.search', {q:'Santiago',countryCode:'CL'}, undefined, 503);
  // Previously selected cities remain usable while the provider is unavailable.
  const cached = await call('auth.register', {...registration,email:`cached-${uuid}@example.com`,locationId});
  check(cached.user.country, 'Chile');
  console.log(`Locations smoke passed: ${checks} checks.`);
} finally { await new Promise(resolve => provider.close(resolve)); }
