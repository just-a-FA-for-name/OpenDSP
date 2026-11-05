const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const net=require('node:net');
const root=path.dirname(__dirname);
const base=JSON.parse(fs.readFileSync(path.join(root,'presets/headphones/sgor-luna-acg-female-v2.json'),'utf8'));
const {definitions}=require('./luna-tuning-definitions.cjs');
function invoke(command){
 return new Promise((resolve,reject)=>{
  const socket=net.createConnection('\\\\.\\pipe\\OpenDSP');
  let data='',settled=false;
  const finish=(error,result)=>{
   if(settled)return;settled=true;socket.destroy();
   error?reject(error):resolve(result);
  };
  socket.setEncoding('utf8');
  socket.setTimeout(8000,()=>finish(new Error('DSP timeout')));
  socket.on('error',error=>finish(error));
  socket.on('close',()=>{if(!settled)finish(new Error('DSP closed before responding'))});
  socket.on('connect',()=>socket.write(JSON.stringify(command)+'\n'));
  socket.on('data',chunk=>{
   data+=chunk;
   const end=data.indexOf('\n');if(end<0)return;
   try{const reply=JSON.parse(data.slice(0,end));if(!reply.ok)throw Error(reply.error||'DSP rejected request');finish(null,reply)}
   catch(error){finish(error)}
  });
 });
}
module.exports={invoke};
if(require.main===module)(async()=>{
 const state=await invoke({cmd:'get_state'});
 if(state.dsd!=='off'||state.engine.dsp_sample_rate!==384000)throw Error('Expected the current 384k PCM context');
 const before=await invoke({cmd:'get_preset'});
 const candidates=[];
 for(const [id,slug,label,description,width,bands] of definitions){
  const preset=structuredClone(base);
  preset.name=`Luna探索 ${id} · ${label}`;
  preset.preamp_db=0;
  preset.output.matrix.width=width;
  preset.filters=bands.map(([type,frequency_hz,gain_db,q])=>({type,frequency_hz,gain_db,q,channel:'both',enabled:true}));
  const preview=await invoke({cmd:'preview_preset',preset,auto_headroom:false});
  candidates.push({id,slug,description,preset:preview.preset});
 }
 const commonPreamp=0; // User-selected reference; headroom analysis remains advisory.
 const source=path.join(root,'presets/headphones/listening-pack');
 const config=path.join(process.env.LOCALAPPDATA,'OpenEQ');
 if(!fs.existsSync(config))throw Error('OpenDSP user directory is missing');
 const library=path.join(config,'presets');
 const distribution=path.join(root,'dist/OpenDSP-Windows-0.4-rc23/presets/listening-pack');
 for(const directory of [source,library,distribution])fs.mkdirSync(directory,{recursive:true});
 const manifest=[];
 for(const item of candidates){
  item.preset.preamp_db=commonPreamp;
  const checked=await invoke({cmd:'preview_preset',preset:item.preset,auto_headroom:false});
  if(checked.preset.preamp_db!==commonPreamp||!Number.isFinite(checked.estimated_peak_gain_db))
   throw Error('Preset validation failed: '+item.preset.name);
  const text=JSON.stringify(checked.preset,null,2)+'\n';
  const file=`${item.id}-${item.slug}.json`;
  const hash=crypto.createHash('sha256').update(checked.preset.name,'utf8').digest('hex').toUpperCase();
  fs.writeFileSync(path.join(source,file),text);
  fs.writeFileSync(path.join(distribution,file),text);
  fs.writeFileSync(path.join(library,hash+'.json'),text);
  manifest.push({file,name:checked.preset.name,description:item.description,width:checked.preset.output.matrix.width,
   preamp_db:commonPreamp,estimated_peak_gain_db:checked.estimated_peak_gain_db});
 }
 const after=await invoke({cmd:'get_preset'});
 if(before.revision!==after.revision)throw Error('The live preset changed while preparing the pack; verify the current selection');
 const guide=[
  '# SGOR Luna / 幻音4X：风格探索试听包','',
  `共 ${manifest.length} 档，统一前级 ${commonPreamp} dB，保留 -1dBFS sample-peak 保护，未改变当前播放预设。`,
  '在 OpenDSP 的「预设管理 ▾」中选取。PCM维持384kHz/32-bit，自己用网易云熟悉的同一曲段对比。','',
  '| 编号 | 名称 | 方向 |','|---|---|---|',
  ...manifest.map((item,index)=>`| ${String(index).padStart(2,'0')} | ${item.name} | ${item.description} |`),'',
  '这些是明显不同的风格方案，不是基于个人耳机实测的 Harman/DF 校正，不保证某个方案一定更好。',
  '前级按用户要求为0dB。提升频段时峰值保护可能介入；不自动改变设备音量。不同曲线仍可能有响度差。',
  '03/05/07 的宽度处理是轻量 M/S 调整；没有反相单边或左右时间差，中置单声道保持不变。',
  '保留之前的 v1、v2 以及调音前原设置，可随时回退。',''
 ].join('\n');
 fs.writeFileSync(path.join(source,'试听说明.md'),guide);
 fs.writeFileSync(path.join(distribution,'试听说明.md'),guide);
 fs.writeFileSync(path.join(root,'evidence/luna-listening-pack-20261023.json'),
  JSON.stringify({count:manifest.length,common_preamp_db:commonPreamp,live_preset_unchanged:true,active:after.preset.name,presets:manifest},null,2));
 console.log(JSON.stringify({saved:manifest.length,commonPreamp,current:after.preset.name,livePresetUnchanged:true,source},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1});
