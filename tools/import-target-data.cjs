const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto');
const root=path.dirname(__dirname),directory=path.join(root,'windows/OpenEQ.Desktop/Assets/Targets');
const commit='7ae0f56d53074872b028649617a22bbb4232feb7';
const entries=[
 ['harman-ie-2016','Harman in-ear 2016.csv','Harman IE 2016','711','in-ear'],
 ['harman-ie-2017-1','Harman in-ear 2017-1.csv','Harman IE 2017-1','711','in-ear'],
 ['harman-ie-2017-2','Harman in-ear 2017-2.csv','Harman IE 2017-2','711','in-ear'],
 ['harman-ie-2019','Harman in-ear 2019.csv','Harman IE 2019','711','in-ear'],
 ['harman-ie-2019-no-bass','Harman in-ear 2019 without bass.csv','Harman IE 2019 · 无低架','711','in-ear'],
 ['harman-oe-2013','Harman over-ear 2013.csv','Harman OE 2013','GRAS','over-ear'],
 ['harman-oe-2015','Harman over-ear 2015.csv','Harman OE 2015','GRAS','over-ear'],
 ['harman-oe-2018','Harman over-ear 2018.csv','Harman OE 2018','GRAS','over-ear'],
 ['harman-oe-2018-no-bass','Harman over-ear 2018 without bass.csv','Harman OE 2018 · 无低架','GRAS','over-ear'],
 ['df-5128','Diffuse field 5128.csv','扩散场 DF · 5128','5128','reference'],
 ['df-5128-tilt','Diffuse field 5128 -1dB per octave.csv','扩散场 5128 · −1dB/oct','5128','reference'],
 ['df-gras','Diffuse field GRAS KEMAR.csv','扩散场 DF · GRAS KEMAR','GRAS-KEMAR','reference'],
 ['df-iso','Diffuse field ISO 11904-1.csv','扩散场 · ISO 11904-1','ISO11904-1','reference'],
 ['jm1-treble','JM-1 with Harman treble filter.csv','JM-1 · Harman高频基础版','5128','reference'],
 ['autoeq-ie','AutoEq in-ear.csv','AutoEq IE 基础目标','711','in-ear']
];
const manifest=[];
for(const [id,filename,name,rig,application] of entries){
 const url=`https://api.github.com/repos/jaakkopasanen/AutoEq/contents/targets/${encodeURIComponent(filename)}?ref=${commit}`;
 const result=cp.spawnSync('curl.exe',['-fsSL','--retry','3','--retry-all-errors','--connect-timeout','15','--max-time','45','-H','User-Agent: OpenDSP-target-import',url],{encoding:'utf8',maxBuffer:4*1024*1024});
 if(result.status!==0)throw Error(filename+': '+result.stderr);
 const document=JSON.parse(result.stdout);if(document.encoding!=='base64')throw Error('Unexpected GitHub encoding');
 const bytes=Buffer.from(document.content,'base64');
 if(!bytes.toString('utf8').startsWith('frequency,'))throw Error('Unexpected CSV header: '+filename);
 fs.writeFileSync(path.join(directory,id+'.csv'),bytes);
 manifest.push({id,name,rig,application,file:id+'.csv',original_file:filename,source:`https://github.com/jaakkopasanen/AutoEq/blob/${commit}/targets/${encodeURIComponent(filename)}`,sha256:crypto.createHash('sha256').update(bytes).digest('hex')});
 console.log(id);
}
fs.writeFileSync(path.join(directory,'catalog.json'),JSON.stringify({commit,license:'MIT; see LICENSE-AutoEq.txt',entries:manifest},null,2)+'\n');
