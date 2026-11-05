// Preference tuning from listening feedback, not an acoustic calibration.
// Moderate-Q sections keep the lower bass intact and separate vocal presence,
// upper-mid hardness, sibilance and upper-treble level. Gains below are sections,
// not the final summed response; preview the complete cascade before applying.
const grid=[
 [95,.95,'中低频冲击量'],[180,1.05,'低频厚度'],[340,1.05,'低中频遮蔽'],
 [700,1.05,'人声厚度'],[1150,1,'人声实体感'],[1850,.95,'女声靠前程度'],
 [2750,1.2,'咬字清晰度'],[3950,1.4,'上中频硬边'],[5250,1.6,'偏低的齿音/金属感'],
 [6650,1.75,'齿音主体'],[8350,1.7,'高频尖锐感'],[10200,1.4,'高频余量'],
 [12500,.707,'极高频整体亮度']
];
function shaped(gains,subBass=0){
 if(gains.length!==grid.length)throw Error('Every region needs an explicit gain');
 const bands=grid.flatMap(([hz,q],i)=>gains[i]===0?[]:[[i===12?'high_shelf':'peak',hz,gains[i],q]]);
 if(subBass!==0)bands.unshift(['low_shelf',45,subBass,.707]);
 return bands;
}
const curves={
 balanced:shaped([-.8,-.7,-.4,.3,.5,.8,.5,-.8,-1,-1.8,-1.3,-.6,-.35]),
 sweet:shaped([-.9,-.8,-.35,.6,1,1.5,.65,-1,-1.15,-2,-1.45,-.65,-.35]),
 bright:shaped([-1,-.85,-.45,.25,.6,1,1.15,-.55,-.8,-1.6,-1.1,-.5,-.2]),
 lean:shaped([-1.4,-1.15,-.7,-.25,.25,.9,.9,-.6,-1,-1.8,-1.35,-.6,-.35]),
 energetic:shaped([-.25,-.6,-.65,.35,.65,1,.7,-.65,-1,-1.7,-1.2,-.6,-.25],.65),
 electronic:shaped([.35,-.4,-.8,-.2,.35,.65,.4,-.85,-1.2,-2.1,-1.5,-.7,-.4],1.4),
 space:shaped([-.55,-.6,-.55,.35,.45,.65,.3,-.7,-1,-1.7,-1.2,-.4,-.15]),
 smooth:shaped([-.65,-.6,-.25,.45,.55,.65,.25,-1.1,-1.5,-2.4,-1.8,-.85,-.6]),
 vocalV1:shaped([-.6,-.55,-.25,.3,.5,.8,.45,-.6,-.85,-1.5,-1.1,-.5,-.3]),
 vocalV2:shaped([-.75,-.65,-.3,.5,.85,1.3,.65,-.8,-1.1,-1.9,-1.4,-.6,-.3]),
 deess:[
  ['peak',4300,-.6,1.25],['peak',5700,-1.2,1.5],['peak',7100,-1.7,1.6],
  ['peak',9300,-.8,1.25],['high_shelf',12000,-.3,.707]
 ],
 bassOnly:[['peak',95,-.7,.95],['peak',165,-.7,1.05],['peak',280,-.45,1.05],['peak',420,-.3,1.1]],
 ends:shaped([-.8,-.7,-.3,0,0,0,0,-.6,-1,-1.6,-1.2,-.5,-.25])
};
const definitions=[
 ['00','reference','原声对照','不加滤波，保留0dB原声参考。',1,[]],
 ['01','balanced-pop','均衡流行','轻收中低频，保留主体厚度，分区抑制高频尖锐感。',1,curves.balanced],
 ['02','sweet-vocal','甜润贴耳女声','细分人声厚度、实体感和靠前程度，压住齿音及高频硬边。',1,curves.sweet],
 ['03','bright-acg','清亮日系','亮度主要来自人声咬字，不再额外抬高本来偏多的高频。',1.06,curves.bright],
 ['04','lean-diffuse','冷静清晰','更收中低频，保留清晰的中频轮廓；不是严格DF校正。',1,curves.lean],
 ['05','energetic-v','燃系V型','收敛原生V型的高频，保留少量鼓点冲击，不再大幅两端加量。',1.08,curves.energetic],
 ['06','electronic-bass','电子低频','少量强调深低频，中低频另行收束，高频保持柔和。',1,curves.electronic],
 ['07','ost-space','OST开阔','保留原有微扩展，少收极高频，减少遮蔽而不堆高频。',1.18,curves.space],
 ['08','smooth','柔顺耐听','更明显但平滑地抑制硬边和齿音，保留中频厚度。',1,curves.smooth]
];
const files={
 'sgor-luna-acg-female-v1.json':curves.vocalV1,
 'sgor-luna-acg-female-v2.json':curves.vocalV2,
 'sgor-luna-acg-female-v2-wide.json':curves.vocalV2,
 'sgor-luna-deess-reference.json':curves.deess,
 'sgor-luna-reference-minus3.json':[],
 'sgor-luna/01.json':curves.bassOnly,
 'sgor-luna/02.json':curves.ends,
 ...Object.fromEntries(definitions.map(([id,slug,_label,_description,_width,bands])=>[`listening-pack/${id}-${slug}.json`,bands]))
};
module.exports={grid,curves,definitions,files};
