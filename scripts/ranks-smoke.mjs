// Only for the disposable local cai_test database. SQL fixtures model already validated history.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
const base = process.env.TEST_API_URL || 'http://localhost:58080';
if (!['localhost','127.0.0.1'].includes(new URL(base).hostname) || !process.env.TEST_PSQL) throw Error('Local API and TEST_PSQL are required; never run against production.');
const sql = query => execFileSync(process.env.TEST_PSQL, ['-h','127.0.0.1','-p','55432','-U','cai_test','-d','cai_test','-X','-v','ON_ERROR_STOP=1','-Atq'], {input:query,encoding:'utf8',env:{...process.env,PGOPTIONS:'-c client_min_messages=warning'}}).trim();
const q = value => `'${String(value).replaceAll("'","''")}'`;
const locationId = randomUUID();
const auditLocationId = randomUUID();
const correctionLocationId = randomUUID();
for (const [id, city] of [[locationId, 'Ciudad de registro'], [auditLocationId, 'Ciudad auditor?a'], [correctionLocationId, 'Ciudad corregida']]) {
  sql(`INSERT INTO city_locations(id,provider_id,country,country_code,city,latitude,longitude) VALUES(${q(id)},(SELECT coalesce(min(provider_id),0)-1 FROM city_locations),'Chile','CL',${q(city)},-33.45,-70.65);`);
}
let checks=0;
const verify=(actual,expected,message) => {assert.deepEqual(actual,expected,message); checks++;};
async function call(action,data={},token,expected=200,file) {
  if(action==='auth.register') data={locationId,...data};
  const headers=token ? {Authorization:`Bearer ${token}`} : {};
  let body;
  if(file) {body=new FormData();body.append('action',action);body.append('data',JSON.stringify(data));body.append('file',file,file.type==='application/pdf' ? 'act.pdf' : 'proof.png');}
  else {headers['Content-Type']='application/json';body=JSON.stringify({action,data});}
  const res=await fetch(`${base}/api`,{method:'POST',headers,body}); const result=await res.json();
  verify(res.status,expected,`${action}: ${JSON.stringify(result)}`); return result;
}
const catalog=JSON.parse(readFileSync(new URL('../catalog/ranks-missions.json',import.meta.url),'utf8'));
const admin=await call('auth.login',{email:process.env.TEST_ADMIN_EMAIL,password:process.env.TEST_ADMIN_PASSWORD});
const at=admin.accessToken;
const testExams={ 'FOR-03':Array.from({length:20},(_,i)=>`Pregunta de prueba ${i+1}`), CREDO:['Pregunta de prueba'], SACRAMENTOS:['Pregunta de prueba'], VIDA:['Pregunta de prueba'], ORACION:['Pregunta de prueba'] };
await call('learning.update',{exams:testExams,courseUrl:'https://example.com/course',waitingGroupUrl:'https://example.com/wait'},at);
sql(`UPDATE users SET role='SOLDADO_INACTIVE' WHERE full_name LIKE 'Rank test %'; UPDATE users SET rank_code='POSTULANTE' WHERE id=${q(admin.user.id)};`);
await call('missions.create',{title:'Unsafe field minimum',description:'Field cannot start at Postulante',evidenceRequirement:'Written record',missionType:'OPERACIONAL',minimumRankCode:'POSTULANTE'},at,400);
const ranks=await call('ranks.get',{},at);
verify(ranks.ranks,catalog.ranks,'All 10 names, thresholds, bonuses, functions and shields come from the workbook');
let missionRows=[];
for(let page=1;;page++) {const m=await call('missions.list',{page,pageSize:100},at);missionRows.push(...m.items);if(page*100>=m.total)break;}
const missions=new Map(missionRows.filter(m=>m.rules.code).map(m=>[m.rules.code,m]));
verify(missions.size,53,'Full 53 mission catalog');
for(const m of catalog.missions) {const actual=missions.get(m.code); verify([actual.title,actual.badgeWeight,actual.rules.evidence,actual.rules.monthlyCap,actual.minimumRankCode],[m.title,m.points,m.evidence,m.monthlyCap,catalog.ranks[m.minimumLevel-1].code],m.code+' exact source values');}
async function member(birth='1990-01-01') {
  const id=randomUUID(); const u=await call('auth.register',{email:`rank-${id}@example.com`,password:`Test-${id}!`,fullName:`Rank test ${id}`});
  await call('users.activate',{id:u.user.id},at);
  if(birth) await call('profile.update',{birthDate:birth},u.accessToken);
  return {id:u.user.id,token:u.accessToken};
}
const reference=await member(); const u=await member();
await call('profile.update',{birthDate:'1980-01-01'},u.token,400);
const reportData={submissionNote:'Bitácora completa: actividad, objeción, respuesta y resultado verificados.',occurredAt:new Date().toISOString(),respectConfirmed:true,privacyConfirmed:true};
const png=new Blob([Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8H0AAAAASUVORK5CYII=','base64')],{type:'image/png'});
const pdf=new Blob(['%PDF-1.4\nTest signed act fixture\n%%EOF'],{type:'application/pdf'});
async function submit(code,who=u,extra={},file) {const m=missions.get(code);await call('missions.assign',{id:m.id},who.token);return call('submissions.create',{...reportData,missionId:m.id,...(['FOR-03','FOR-04'].includes(code) ? {examAnswers:testExams[code==='FOR-03' ? code : extra.moduleCode].map(()=> 'Respuesta de prueba revisable')} : {}),...extra},who.token,200,file);}
async function approve(s,extra={}) {return call('submissions.review',{id:s.id,status:'APPROVED',requirementsVerified:true,...extra},at);}
const first=await submit('PRX-01');
await call('submissions.review',{id:first.id,status:'APPROVED'},at,400);
await approve(first);
await call('missions.assign',{id:missions.get('PRX-02').id},u.token); // Prerequisite exception resolves the circular requirement.
await approve(await submit('PRX-02'));
let progress=await call('progress.get',{},u.token);
verify([progress.rankCode,progress.totalPoints,progress.milestones.includes('HIT-DOM')],['POSTULANTE',125,true],'Domestic milestone awards 25 but entry still blocks ascent');
const entry={userId:u.id,code:'HIT-ING',requirementsVerified:true,interviewVerified:true,referenceMemberId:reference.id,reviewNote:'Entrevista y referencia verificadas'};
await call('milestones.validate',entry,at,400,pdf);
sql(`UPDATE users SET formation_started_at=CURRENT_DATE-31 WHERE id=${q(u.id)};`);
await call('milestones.validate',entry,at,200,pdf);
progress=await call('progress.get',{},u.token);
verify([progress.rankCode,progress.totalPoints],['COMPANERO_ARMAS',135],'Entry + domestic milestone grants rank 2 and exactly one bonus');
await call('milestones.validate',entry,at,409,pdf);
await call('files.get',{id:sql(`SELECT file_id FROM user_milestones WHERE user_id=${q(u.id)} AND code='HIT-ING';`)},u.token,404);
await approve(await submit('PRX-03'));
const registry=await call('sectReports.create',{sectName:`New sect ${randomUUID()}`,locationDescription:'Public venue',referenceNote:'Documented doctrine and sources',latitude:-12.0464,longitude:-77.0428},u.token);
await call('sectReports.review',{id:registry.id,status:'APPROVED'},at);
await call('sectReports.create',{sectName:'Invalid coordinates',locationDescription:'x',referenceNote:'x',latitude:500,longitude:0},u.token,400);
const cartography=await submit('CAR-01',u,{sectReportId:registry.id},png);
await approve(cartography,{firstRegistryBonus:true,sectReportId:registry.id});
progress=await call('progress.get',{},u.token);
verify([progress.rankCode,progress.totalPoints],['ESCUDERO',295],'Proximity unlocks rank 3 with hito, primicia and promotion bonuses');
const map=await call('sectRegistry.list',{pageSize:100},u.token);
verify(map.items.some(s=>s.id===registry.id && s.latitude===-12.0464),true,'Real approved map coordinates');
await call('submissions.list',{},u.token,403);
await call('files.get',{id:sql(`SELECT file_id FROM mission_submissions WHERE id=${q(cartography.id)};`)},u.token,404);
const primiciaUser=await member();
sql(`UPDATE users SET rank_code='COMPANERO_ARMAS' WHERE id=${q(primiciaUser.id)};`);
for(let i=0;i<4;i++) {
  const fiche=await call('sectReports.create',{sectName:`Unique public group ${randomUUID()}`,locationDescription:'Public venue',referenceNote:'Doctrine and sources',latitude:1,longitude:1},primiciaUser.token);
  await call('sectReports.review',{id:fiche.id,status:'APPROVED'},at);
  await approve(await submit('CAR-01',primiciaUser,{sectReportId:fiche.id},png),{firstRegistryBonus:true,sectReportId:fiche.id});
}
verify((await call('progress.get',{},primiciaUser.token)).totalPoints,180,'CAR-01 cap includes primicia bonuses');
const capUser=await member();
for(let i=0;i<4;i++) await approve(await submit('FOR-02',capUser),{excellent:i===0});
verify((await call('progress.get',{},capUser.token)).totalPoints,120,'Monthly cap clips quality and repeated mission points');
for(let i=0;i<9;i++) await approve(await submit('VIG-03',capUser,{honorReport:true}));
verify((await call('progress.get',{},capUser.token)).totalPoints,180,'Honor reports use fractional 7.5 points; cap remains 60');
await approve(await submit('VIG-02',capUser,{honorReport:true}));
await call('submissions.create',{...reportData,missionId:missions.get('VIG-02').id,honorReport:true},capUser.token,409);
await approve(await submit('FOR-01',capUser,{},pdf));
await call('submissions.create',{...reportData,missionId:missions.get('FOR-01').id},capUser.token,409);
const consistent=await member();
sql(`INSERT INTO mission_submissions(mission_id,user_id,status,occurred_at,submission_note) SELECT m.id,${q(consistent.id)},'APPROVED',(date_trunc('week',now() AT TIME ZONE 'America/Lima')-n*interval '1 week'+interval '12 hours') AT TIME ZONE 'America/Lima','Trusted validated weekly fixture' FROM missions m CROSS JOIN generate_series(1,3)n WHERE m.catalog_code='HOS-01';`);
await approve(await submit('HOS-01',consistent)); await approve(await submit('HOS-01',consistent));
verify(sql(`SELECT count(*)||':'||sum(points)::text FROM point_ledger WHERE user_id=${q(consistent.id)} AND kind='CONSISTENCY';`),'1:25.00','Four-week consistency bonus is awarded once, without overlapping payouts');
await approve(await submit('FOR-03',u));
for(const moduleCode of ['CREDO','SACRAMENTOS','VIDA','ORACION']) await approve(await submit('FOR-04',u,{moduleCode}));
progress=await call('progress.get',{},u.token);
verify(progress.rankCode,'ESCUDERO','Hito and enough points still require recent Hospitalidad for rank 4');
await call('submissions.create',{...reportData,missionId:missions.get('FOR-04').id,moduleCode:'CREDO'},u.token,409);
await approve(await submit('HOS-01',u));
progress=await call('progress.get',{},u.token);
verify(progress.rankCode,'SARGENTO_ARMAS','Four distinct modules + Bible exam + recent Hospitalidad unlock rank 4');
const minor=await member('2012-01-01');
await call('missions.assign',{id:missions.get('FOR-01').id},minor.token,403);
await call('profile.update',{birthDate:'2012-01-01',parentalConsentVerified:true},minor.token);
await call('missions.assign',{id:missions.get('FOR-01').id},minor.token,403);
await call('profile.update',{userId:minor.id,birthDate:'2012-01-01',parentalConsentVerified:true,reviewNote:'Acta parental verificada'},at);
const minorCatalog=await call('missions.list',{pageSize:100},minor.token);
verify(minorCatalog.items.every(m=>/^(FOR|VIG|HOS)-/.test(m.rules.code)),true,'Minor catalog limited to Formation, Vigilia and Hospitality');
await call('missions.assign',{id:missions.get('PRX-01').id},minor.token,403);
await call('missions.assign',{id:missions.get('FOR-01').id},minor.token);
await call('sectReports.create',{sectName:'Minor fixture',locationDescription:'Public fixture',referenceNote:'Doctrine fixture'},minor.token,403);
const unknown=await member(null);
await call('missions.assign',{id:missions.get('FOR-02').id},unknown.token);
await call('submissions.create',{...reportData,missionId:missions.get('FOR-02').id},unknown.token,400);
// Safety is checked using fixed America/Lima time, regardless of client offset.
const day=new Date(Date.now()-86400000).toISOString().slice(0,10);
const fieldMission=missions.get('ESC-04');
await call('missions.assign',{id:fieldMission.id},u.token);
const fieldData={...reportData,missionId:fieldMission.id,occurredAt:`${day}T12:00:00-05:00`,endedAt:`${day}T13:00:00-05:00`,safeFieldConfirmed:true,noVulnerableTargets:true,companionId:u.id};
await call('submissions.create',fieldData,u.token,400);
await call('submissions.create',{...fieldData,companionId:reference.id,occurredAt:`${day}T21:00:00-05:00`,endedAt:`${day}T22:00:00-05:00`},u.token,400);
await call('submissions.create',{...fieldData,companionId:reference.id,recordingIncluded:true,evidenceUrl:'https://example.com/video'},u.token,400);
await call('submissions.create',{...fieldData,companionId:reference.id,safeFieldConfirmed:false},u.token,400);
await call('submissions.create',{...fieldData,companionId:reference.id,occurredAt:`${day}T12:00:00`,endedAt:`${day}T13:00:00`},u.token,400);
const safe=await call('submissions.create',{...fieldData,companionId:reference.id,occurredAt:`${day}T17:00:00Z`,endedAt:`${day}T18:00:00Z`},u.token);
await approve(safe);
const commander=await member();
sql(`UPDATE users SET rank_code='COMENDADOR' WHERE id=${q(commander.id)}; UPDATE users SET sponsor_id=${q(commander.id)} WHERE id=${q(reference.id)};`);
const own=await call('missions.create',{title:'Own mission',description:'Formation organized by rank 6',evidenceRequirement:'Signed record',missionType:'FORMATIVA',minimumRankCode:'POSTULANTE',badgeWeight:50},commander.token);
await call('missions.publish',{id:own.id},commander.token,403);
await call('missions.publish',{id:own.id},at);
const team=await submit('ESC-04',commander,{...fieldData,missionId:missions.get('ESC-04').id,companionId:reference.id});
await approve(team,{teamBonus:true});
verify((await call('progress.get',{},commander.token)).totalPoints,75,'Team bonus requires a registered lower-rank sponsored companion');
await approve(await submit('FOR-02',commander,{companionId:reference.id}),{teamBonus:true});
verify((await call('progress.get',{},commander.token)).totalPoints,130,'Team bonus also applies to formation activities with a registered sponsored companion');
await call('missions.assign',{id:missions.get('DEB-04').id},commander.token);
const inviteData={...fieldData,companionId:reference.id,missionId:missions.get('DEB-04').id,recordingIncluded:true,recordingConsent:true,evidenceUrl:'https://example.com/consented-video'};
await call('submissions.create',inviteData,commander.token,400);
const invitation=await call('evidence.upload',{},commander.token,200,pdf);
await call('files.get',{id:invitation.id},commander.token,404);
await approve(await call('submissions.create',{...inviteData,invitationFileId:invitation.id},commander.token));
await call('milestones.validate',{userId:commander.id,code:'HIT-PRE',requirementsVerified:true,reviewNote:'Missing PRE-03 and PRE-01'},at,400,pdf);
sql(`INSERT INTO spiritual_records(user_id,kind,day) SELECT ${q(capUser.id)},'PRAYER',(now() AT TIME ZONE 'America/Lima')::date-n FROM generate_series(0,6)n;`);
const pendingAct=await submit('VIG-01',capUser);
await call('submissions.review',{id:pendingAct.id,status:'REJECTED',rejectionReason:'DISRESPECT',reviewNote:'Burla documentada'},at);
verify(sql(`SELECT points FROM point_ledger WHERE source_key='penalty:${pendingAct.id}';`),'-20.00','Disrespect automatically subtracts 20');
sql(`UPDATE users SET rank_code='SARGENTO_ARMAS' WHERE id=${q(capUser.id)};`);
for(let i=0;i<2;i++) {const s=await submit('VIG-01',capUser);await call('submissions.review',{id:s.id,status:'REJECTED',rejectionReason:'FALSE_EVIDENCE',reviewNote:'Evidencia falsa verificada'},at);}
const punished=await call('progress.get',{},capUser.token);
verify([punished.totalPoints,punished.rankCode],[0,'ESCUDERO'],'False evidence removes monthly points and second offense demotes one rank');
verify(sql(`SELECT rank_ceiling FROM users WHERE id=${q(capUser.id)};`),'3','Sanction prevents instant automatic re-promotion');
// Boundary tests: below and at every one of the nine ascending thresholds.
for(let level=2;level<=10;level++) {
  const b=await member(); const rank=catalog.ranks[level-1]; const previous=catalog.ranks[level-2];
  sql(`UPDATE users SET rank_code=${q(previous.code)} WHERE id=${q(b.id)};
       INSERT INTO user_milestones(user_id,code,note) SELECT ${q(b.id)},code,'Trusted fixture: already validated history' FROM cai_milestones WHERE level<=${level};
       INSERT INTO point_ledger(user_id,source_key,kind,points,description) VALUES(${q(b.id)},'boundary','TEST',${rank.threshold-1},'Boundary fixture');
       INSERT INTO mission_submissions(mission_id,user_id,status,submission_note) SELECT id,${q(b.id)},'APPROVED','Trusted recent Hospitalidad fixture' FROM missions WHERE catalog_code='HOS-01';`);
  let p=await call('progress.get',{},b.token); verify(p.rankCode,previous.code,`Below threshold ${level}`);
  sql(`UPDATE point_ledger SET points=${rank.threshold} WHERE user_id=${q(b.id)} AND source_key='boundary';`);
  const concurrent=await Promise.all([call('progress.get',{},b.token),call('progress.get',{},b.token)]);
  p=concurrent[0]; verify(concurrent[1].rankCode,p.rankCode,`Concurrent progress ${level} reads the promoted rank consistently`);
  verify([p.rankCode,p.totalPoints],[rank.code,rank.threshold+rank.bonus],`At threshold ${level}, with both keys and exact bonus`);
  const repeat=await call('progress.get',{},b.token); verify(repeat.totalPoints,p.totalPoints,`Promotion ${level} is idempotent`);
  sql(`UPDATE users SET role='SOLDADO_INACTIVE' WHERE id=${q(b.id)};`); // Keep initial admin-validation phase isolated.
}
const voteLess=await call('ranks.get',{},at);
verify(voteLess.milestones.find(h=>h.code==='HIT-GM').requirement.includes('Voto'),false,'No vow is tracked as a mission or progress item');
const complaint=await call('conduct.create',{targetId:commander.id,note:'Humillación de un hermano de rango inferior, con testigos.'},reference.token);
await call('conduct.list',{},reference.token,403);
await call('conduct.review',{id:complaint.id,upheld:true,reviewNote:'Decisión del Capítulo, hechos verificados.'},at);
verify((await call('progress.get',{},commander.token)).rankCode,'POSTULANTE','Verified humiliation removes the high rank');
await call('profile.update',{userId:u.id,birthDate:'1990-01-01',reserve:true,reviewNote:'Reserva de prueba'},at);
const reserved=await call('progress.get',{},u.token);
verify([reserved.rankCode,reserved.totalPoints,reserved.reserve],[progress.rankCode,progress.totalPoints+60,true],'Reserve preserves points and rank');
await call('missions.assign',{id:missions.get('FOR-02').id},u.token,409);
await call('profile.update',{userId:u.id,birthDate:'1990-01-01',reserve:false,reviewNote:'Reincorporación verificada'},at);
await call('missions.assign',{id:missions.get('FOR-02').id},u.token);
// Once three commanders exist, an administrator also needs the right rank; soldiers never gain evidence access.
const senior=[];
for(let i=0;i<3;i++) {const v=await member();sql(`UPDATE users SET rank_code='COMENDADOR' WHERE id=${q(v.id)};`);senior.push(v);}
const lowerSubmission=await submit('FOR-02',reference);
await call('submissions.review',{id:lowerSubmission.id,status:'APPROVED',requirementsVerified:true},at,403);
await call('submissions.review',{id:lowerSubmission.id,status:'APPROVED',requirementsVerified:true,foundingValidation:true},at,400);
await call('submissions.review',{id:lowerSubmission.id,status:'APPROVED',requirementsVerified:true,foundingValidation:true,reviewNote:'Founding Chapter validation; no qualified administrator exists.'},at);
sql(`UPDATE users SET rank_code='SENESCAL' WHERE id=${q(admin.user.id)};`);
const strictSubmission=await submit('FOR-02',reference); await approve(strictSubmission);
const blockedFounding=await submit('FOR-02',reference);
await call('users.setRole',{id:senior[0].id,role:'REGISTRADOR'},at);
sql(`UPDATE users SET rank_code='SENESCAL' WHERE id=${q(senior[0].id)}; UPDATE users SET rank_code='POSTULANTE' WHERE id=${q(admin.user.id)};`);
await call('submissions.review',{id:blockedFounding.id,status:'APPROVED',requirementsVerified:true,foundingValidation:true,reviewNote:'Attempt to bypass a qualified Senescal'},at,403);
await call('submissions.review',{id:lowerSubmission.id,status:'APPROVED',requirementsVerified:true},senior[1].token,403);
sql(`UPDATE users SET rank_code='POSTULANTE' WHERE id=${q(admin.user.id)}; UPDATE users SET role='SOLDADO_INACTIVE' WHERE id IN(${senior.map(s=>q(s.id)).join(',')});`);
// Reapplying the new scripts must not reset earned ranks or award catalog/legacy points twice.
const before=sql('SELECT count(*) FROM point_ledger;');
for(const name of ['003_rank_system.sql','004_catalog.sql']) sql(readFileSync(new URL('../database/'+name,import.meta.url),'utf8'));
verify(sql('SELECT count(*) FROM point_ledger;'),before,'Reapplying migrations does not duplicate points');
verify(sql('SELECT count(*) FROM missions WHERE catalog_code IS NOT NULL;'),'53','Reapplying seed does not duplicate missions');
verify((await call('progress.get',{},u.token)).rankCode,'SARGENTO_ARMAS','Reapplying migration preserves earned rank');
// Workbook additions: real filters, configured exams, daily records and stale alerts.
const workbookMember=await member();
await call('learning.update',{exams:testExams},workbookMember.token,403);
await call('learning.update',{exams:testExams,courseUrl:'http://example.com'},at,400);
await call('profile.update',{birthDate:'1990-01-01',locationId:auditLocationId},workbookMember.token);
const filtered=await call('users.list',{city:'Ciudad auditor?a',role:'SOLDADO_ACTIVE'},at);
verify(filtered.items.map(m=>m.id),[workbookMember.id],'City filter uses real profile data');
const rankFilter=await call('users.list',{city:'Ciudad auditor?a',rankCode:'MARISCAL'},at);
verify(rankFilter.total,0,'Rank filters apply before pagination');
await call('missions.assign',{id:missions.get('FOR-03').id},workbookMember.token);
await call('learning.update',{exams:{}},at);
await call('submissions.create',{...reportData,missionId:missions.get('FOR-03').id},workbookMember.token,409);
await call('learning.update',{exams:testExams},at);
await call('submissions.create',{...reportData,missionId:missions.get('FOR-03').id,examAnswers:['Solo una respuesta']},workbookMember.token,400);
const examSubmission=await submit('FOR-03',workbookMember);
verify(sql(`SELECT jsonb_array_length(details->'exam'->'questions') FROM mission_submissions WHERE id=${q(examSubmission.id)};`),'20','Exam snapshots preserve all twenty questions');
await call('files.get',{id:examSubmission.id},workbookMember.token,404);
const limaDay=new Intl.DateTimeFormat('en-CA',{timeZone:'America/Lima'}).format(new Date());
await call('missions.assign',{id:missions.get('VIG-01').id},workbookMember.token);
await call('submissions.create',{...reportData,missionId:missions.get('VIG-01').id},workbookMember.token,400);
for(let n=0;n<7;n++) {const d=new Date(limaDay+'T12:00:00Z');d.setUTCDate(d.getUTCDate()-n);await call('spiritual.record',{kind:'PRAYER',day:d.toISOString().slice(0,10)},workbookMember.token);}
await call('spiritual.record',{kind:'PRAYER',day:limaDay},workbookMember.token);
verify((await call('spiritual.list',{},workbookMember.token)).items.length,7,'Duplicate day is idempotent');
await approve(await submit('VIG-01',workbookMember));
await call('submissions.create',{...reportData,missionId:missions.get('VIG-01').id},workbookMember.token,409);
await call('spiritual.record',{kind:'ROSARY',day:limaDay},workbookMember.token);
await approve(await submit('VIG-06',workbookMember));
await call('submissions.create',{...reportData,missionId:missions.get('VIG-06').id},workbookMember.token,409);
const oldActivity=sql(`UPDATE users SET activity_resumed_at=now()-interval '130 days' WHERE id=${q(workbookMember.id)} RETURNING activity_resumed_at;`);
await call('profile.update',{userId:workbookMember.id,birthDate:'1990-01-01',locationId:correctionLocationId,reviewNote:'Solo cambio de ciudad'},at);
verify(sql(`SELECT activity_resumed_at FROM users WHERE id=${q(workbookMember.id)};`),oldActivity,'Editing an active profile does not restart inactivity');
sql(`INSERT INTO activity_alerts(user_id,last_mission_at,days_inactive) VALUES(${q(workbookMember.id)},now()-interval '130 days',130) ON CONFLICT(user_id) DO UPDATE SET days_inactive=130;`);
const monitorSource=readFileSync(new URL('../Infrastructure/ActivityMonitor.cs',import.meta.url),'utf8');
const monitorSql=monitorSource.split('source.CreateCommand("""')[1].split('""");')[0];
sql(monitorSql);
verify(sql(`SELECT reserve FROM users WHERE id=${q(workbookMember.id)};`),'f','A stale 130-day alert does not reserve a member with fresh activity');
verify(sql(`SELECT count(*) FROM activity_alerts WHERE user_id=${q(workbookMember.id)};`),'0','Fresh activity removes the stale alert');
const armor=await call('certificates.create',{certificateNumber:'ARM-'+randomUUID(),issuedToName:'Acta de prueba',kind:'ARMOR'},at,200,pdf);
verify(sql(`SELECT kind FROM certificates WHERE id=${q(armor.id)};`),'ARMOR','Certificates record their workbook type');

console.log(`OK: ${checks} rank-system checks on the disposable local cai_test database.`);
