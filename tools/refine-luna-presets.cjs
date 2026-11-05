const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {isDeepStrictEqual}=require('node:util');
const {invoke}=require('./create-luna-listening-pack.cjs');
const {files,definitions,grid}=require('./luna-tuning-definitions.cjs');
const root=path.dirname(__dirname),source=path.join(root,'presets/headphones');
const library=path.join(process.env.LOCALAPPDATA,'OpenEQ/presets');
const distribution=path.join(root,'dist/OpenDSP-Windows-0.4-rc23/presets');
const backup=path.join(root,'build','luna-refinement-'+new Date().toISOString().replace(/[:.]/g,'-'));
const read=file=>JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));
const hash=name=>crypto.createHash('sha256').update(name,'utf8').digest('hex').toUpperCase()+'.json';
const filters=bands=>bands.map(([type,frequency_hz,gain_db,q])=>({type,frequency_hz,gain_db,q,channel:'both',enabled:true}));
function backupFile(file,relative){
 if(!fs.existsSync(file))return;
 const copy=path.join(backup,relative);fs.mkdirSync(path.dirname(copy),{recursive:true});fs.copyFileSync(file,copy);
}
function write(file,value){
 fs.mkdirSync(path.dirname(file),{recursive:true});
 const temporary=file+'.refine.tmp';fs.writeFileSync(temporary,JSON.stringify(value,null,2)+'\n');fs.renameSync(temporary,file);
}
function inspect(preview){
 if(preview.preset.preamp_db!==0||!Number.isFinite(preview.estimated_peak_gain_db))throw Error('Invalid preset preview');
 const points=preview.eq_response_hz_left_right_db;
 if(!Array.isArray(points))throw Error('Preview did not include a response curve');
 if(points.some(row=>row.some(value=>!Number.isFinite(value))))throw Error('Non-finite response');
 const gains=points.flatMap(row=>row.slice(1));
 if(Math.max(...gains)>4||Math.min(...gains)<-6)throw Error('Excessive combined EQ excursion: '+preview.preset.name);
 return {filters:preview.preset.filters.length,peak_eq_db:Math.max(...gains),minimum_eq_db:Math.min(...gains),
  estimated_peak_gain_db:preview.estimated_peak_gain_db,suggested_preamp_db:preview.suggested_preamp_db,
  anchors:[30,100,180,1000,2000,4000,6500,8500,12000].map(hz=>points.reduce((a,b)=>Math.abs(Math.log(b[0]/hz))<Math.abs(Math.log(a[0]/hz))?b:a)),
  response:points};
}
async function candidate(original,bands){
 const preset=structuredClone(original);preset.preamp_db=0;preset.filters=filters(bands);
 const preview=await invoke({cmd:'preview_preset',preset,auto_headroom:false});
 return {preset,analysis:inspect(preview)};
}
(async()=>{
 const state=await invoke({cmd:'get_state'}),before=await invoke({cmd:'get_preset'});
 if(state.dsd!=='off'||state.engine.dsp_sample_rate!==384000)throw Error('Expected 384k PCM');
 fs.mkdirSync(backup,{recursive:true});write(path.join(backup,'active.json'),before);
 write(path.join(backup,'active-preset-before.json'),before.preset);
 const plans=[],byName=new Map();
 // Validate every proposed source/library variant before changing any preset.
 for(const [relative,bands] of Object.entries(files)){
  const sourceFile=path.join(source,relative),original=read(sourceFile);
  const libraryFile=path.join(library,hash(original.name));
  const saved=fs.existsSync(libraryFile)?read(libraryFile):original;
  const next=await candidate(original,bands),local=await candidate(saved,bands);
  if(bands.length>0&&!relative.endsWith('/01.json')){
   const treble=next.analysis.response.filter(row=>row[0]>=4500&&row[0]<=12000);
   if(treble.some(row=>row[1]>.05||row[2]>.05))throw Error('Treble unexpectedly boosted: '+original.name);
  }
  if(bands.length===0&&next.analysis.response.some(row=>Math.abs(row[1])>1e-8||Math.abs(row[2])>1e-8))throw Error('Reference must stay flat');
  plans.push({relative,sourceFile,libraryFile,original,next,local});byName.set(original.name,bands);
 }
 const activeBands=byName.get(before.preset.name);
 const active=activeBands?await candidate(before.preset,activeBands):null;
 for(const plan of plans){
  backupFile(plan.sourceFile,path.join('source',plan.relative));
  backupFile(plan.libraryFile,path.join('library',path.basename(plan.libraryFile)));
  backupFile(path.join(distribution,plan.relative),path.join('distribution',plan.relative));
  write(plan.sourceFile,plan.next.preset);write(plan.libraryFile,plan.local.preset);write(path.join(distribution,plan.relative),plan.next.preset);
 }
 let applied=null;
 if(active){
  // Use the original revision: never overwrite a manual edit made during preparation.
  applied=await invoke({cmd:'apply_preset',preset:active.preset,expected_revision:before.revision,auto_headroom:false});
  if(!isDeepStrictEqual(applied.preset.filters,active.preset.filters)||!isDeepStrictEqual(applied.preset.output,before.preset.output))
   throw Error('Applied preset differs from the requested EQ/output settings');
 }
 const after=await invoke({cmd:'get_preset'}),afterState=await invoke({cmd:'get_state'});
 if(afterState.engine.dsp_sample_rate!==384000||afterState.output_format!==state.output_format||
    afterState.input_id!==state.input_id||afterState.output_id!==state.output_id)throw Error('Audio context changed during refinement');
 const guide=[
  '# SGOR Luna / 幻音4X：细化试听包（2026-10-23）','',
  '按用户实听反馈调整：原声两端略多，低频下潜良好，高频偏多且尖。前级保持0dB。',
  '主力方案使用13–14段中等Q滤波，分开控制中低频、人声、硬边、齿音与极高频。不是耳机单体实测校准。','',
  '| 编号 | 名称 | 这次的侧重 |','|---|---|---|',
  ...definitions.map(([id,,label,description])=>`| ${id} | ${label} | ${description} |`),'',
  '00和单独的原声对照仍然完全平直。05/06仅保留少量风格性深低频提升，其他主力方案不抬深低频。',
  '不要把这些滤波器继续叠加到旧EQ上：软件库中的同名方案已经替换。',
  '保持原有宽度、压缩器、延时、输出增益和峰值保护设置；同前级不等于严格等响。',
  '正增益仍可能触发已有峰值保护。先听当前女声档，再对照00；重点反馈齿音、暗闷感和人声厚度。',
  '调节点用途：'+grid.map(([hz,,purpose])=>`${hz}Hz ${purpose}`).join('；')+'。',
  '8kHz附近及更高频受佩戴和耳套影响明显，此处采用适度宽带调整，不假定有已测出的精确共振峰。',''
 ].join('\n');
 fs.writeFileSync(path.join(source,'listening-pack/试听说明.md'),guide);
 fs.writeFileSync(path.join(distribution,'listening-pack/试听说明.md'),guide);
 const report={result:'PASS',backup,changed_profiles:plans.filter(p=>p.next.preset.filters.length>0).length,
  flat_references:plans.filter(p=>p.next.preset.filters.length===0).length,preamp_db:0,
  active_applied:!!applied,active:after.preset.name,active_filters:after.preset.filters.length,output_format:afterState.output_format,
  profiles:plans.map(p=>({file:p.relative,name:p.next.preset.name,previous_filters:p.original.filters.length,...p.next.analysis})),
  active_analysis:active?.analysis??null,revision:after.revision};
 write(path.join(root,'evidence/luna-refinement-20261023.json'),report);
 console.log(JSON.stringify({result:report.result,updated:report.changed_profiles,references:report.flat_references,activeApplied:report.active_applied,
  active:report.active,filters:report.active_filters,preamp:0,output:report.output_format,backup},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1});
