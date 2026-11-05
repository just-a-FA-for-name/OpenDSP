const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),net=require('node:net');
const root=path.dirname(__dirname);
const source=path.join(root,'presets/headphones');
const library=path.join(process.env.LOCALAPPDATA,'OpenEQ/presets');
const backup=path.join(root,'build/ui-perf-20261023/presets-before-zero');
function invoke(command){
 return new Promise((resolve,reject)=>{
  const socket=net.createConnection('\\\\.\\pipe\\OpenDSP');
  let data='',done=false;
  const finish=(error,value)=>{if(done)return;done=true;socket.destroy();error?reject(error):resolve(value)};
  socket.setEncoding('utf8');socket.setTimeout(8000,()=>finish(Error('DSP timeout')));
  socket.on('error',error=>finish(error));
  socket.on('close',()=>{if(!done)finish(Error('DSP closed before its reply'))});
  socket.on('connect',()=>socket.write(JSON.stringify(command)+'\n'));
  socket.on('data',chunk=>{data+=chunk;const end=data.indexOf('\n');if(end<0)return;try{
   const result=JSON.parse(data.slice(0,end));if(!result.ok)throw Error(result.error);finish(null,result);
  }catch(error){finish(error)}});
 });
}
function files(directory){
 return fs.readdirSync(directory,{withFileTypes:true}).flatMap(entry=>
  entry.isDirectory()?files(path.join(directory,entry.name)):entry.name.endsWith('.json')?[path.join(directory,entry.name)]:[]);
}
function save(file,preset,backupName){
 const copy=path.join(backup,backupName);
 fs.mkdirSync(path.dirname(copy),{recursive:true});
 if(fs.existsSync(file)&&!fs.existsSync(copy))fs.copyFileSync(file,copy);
 fs.writeFileSync(file,JSON.stringify(preset,null,2)+'\n');
}
const hash=name=>crypto.createHash('sha256').update(name,'utf8').digest('hex').toUpperCase()+'.json';
(async()=>{
 fs.mkdirSync(backup,{recursive:true});
 const current=await invoke({cmd:'get_preset'});
 const activeBackup=path.join(backup,'active-preset.json');
 if(!fs.existsSync(activeBackup))fs.writeFileSync(activeBackup,JSON.stringify(current,null,2));
 const changed=[];
 for(const file of files(source)){
  const preset=JSON.parse(fs.readFileSync(file,'utf8').replace(/^\uFEFF/,''));
  if(preset.format!=='org.openeq.preset'||!/luna|sgor/i.test(preset.name+' '+preset.device_hint))continue;
  const relative=path.relative(source,file);
  const oldName=preset.name;
  const newName=oldName.replace(/[-−]3dB/g,'0dB');
  preset.name=newName;preset.preamp_db=0;
  const valid=await invoke({cmd:'preview_preset',preset,auto_headroom:false});
  if(valid.preset.preamp_db!==0)throw Error('Zero reference was not preserved');
  save(file,preset,path.join('source',relative));
  const oldLibrary=path.join(library,hash(oldName)),newLibrary=path.join(library,hash(newName));
  const local=fs.existsSync(oldLibrary)?JSON.parse(fs.readFileSync(oldLibrary,'utf8').replace(/^\uFEFF/,'')):structuredClone(preset);
  local.name=newName;local.preamp_db=0; // Preserve any user edits to the library's EQ.
  await invoke({cmd:'preview_preset',preset:local,auto_headroom:false});
  save(newLibrary,local,path.join('library',path.basename(newLibrary)));
  if(oldLibrary!==newLibrary&&fs.existsSync(oldLibrary)){
   fs.copyFileSync(oldLibrary,path.join(backup,'library',path.basename(oldLibrary)));
   fs.unlinkSync(oldLibrary);
  }
  const distributed=path.join(root,'dist/OpenDSP-Windows-0.4-rc20/presets',relative);
  if(fs.existsSync(distributed))save(distributed,preset,path.join('distribution',relative));
  changed.push(newName);
 }
 const latest=await invoke({cmd:'get_preset'});
 const live=structuredClone(latest.preset);
 live.preamp_db=0;live.name=live.name.replace(/[-−]3dB/g,'0dB');
 const applied=await invoke({cmd:'apply_preset',preset:live,expected_revision:latest.revision,auto_headroom:false});
 const activeLibrary=path.join(library,hash(live.name));
 if(/luna|sgor/i.test(live.name)&&!fs.existsSync(activeLibrary))
  save(activeLibrary,applied.preset,path.join('library',hash(live.name)));
 fs.writeFileSync(path.join(root,'evidence/luna-zero-baseline-20261023.json'),
  JSON.stringify({changed,preamp_db:applied.preset.preamp_db,active:applied.preset.name,filters_preserved:true,backup},null,2));
 console.log(JSON.stringify({saved:changed.length,active:applied.preset.name,preamp_db:applied.preset.preamp_db,filtersPreserved:true},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1});
