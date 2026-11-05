#pragma once
#include <windows.h>
#include <cstdint>
#include <cmath>
#include <algorithm>
#include <vector>
namespace openeq {
// Shared ABI: UI publishes one bounded snapshot. The audio callback performs no file I/O.
// v2: full DSP rack (DC / matrix+width / compressor) fields appended; math mirrors
// windows/OpenEQ.Desktop/Dsp.cs StereoProcessor sample-for-sample (double precision).
struct Filter {uint32_t type,enabled,channel,reserved;double hz,gain,q;};
struct Config {
 uint32_t active,enabled,count,flags;double preamp,output,balance,delayL,delayR,ceiling;
 // rack: compressor (立体声联动) + 矩阵 + 宽度
 double compThreshold,compRatio,compAttackMs,compReleaseMs,compKnee,compMakeup;
 double mLL,mLR,mRL,mRR,width;
 Filter filters[32];
};
struct Shared {uint32_t magic,version;volatile LONG sequence;uint32_t size;Config config;volatile LONG64 frames;double inL,inR,outL,outR;uint32_t rate,channels,appliedSequence,pid;uint64_t clipped,protectedFrames;uint64_t heartbeat;uint32_t controller,leaseExpired;};
static_assert(sizeof(Config)==1432&&offsetof(Shared,frames)==1448&&sizeof(Shared)==1536,"Shared configuration ABI changed");
constexpr uint32_t Magic=0x5145504f;
constexpr uint32_t AbiVersion=2;
// flags bits: 0=protection 1=invertL 2=invertR 3=dcBlock 4=compressor
inline bool valid(const Config& c){auto finite=[](double v,double min,double max){return std::isfinite(v)&&v>=min&&v<=max;};if(c.active>1||c.enabled>1||c.count>32||c.flags>31||!finite(c.preamp,-60,0)||!finite(c.output,-60,0)||!finite(c.balance,-1,1)||!finite(c.delayL,0,100)||!finite(c.delayR,0,100)||!finite(c.ceiling,-12,0))return false;
 if((c.flags&16)&&(!finite(c.compThreshold,-60,0)||!finite(c.compRatio,1,20)||!finite(c.compAttackMs,.1,200)||!finite(c.compReleaseMs,5,2000)||!finite(c.compKnee,0,24)||!finite(c.compMakeup,-12,24)))return false;
 if(!finite(c.mLL,-2,2)||!finite(c.mLR,-2,2)||!finite(c.mRL,-2,2)||!finite(c.mRR,-2,2)||!finite(c.width,0,2))return false;
 for(uint32_t n=0;n<c.count;n++){auto& f=c.filters[n];if(f.type>6||f.enabled>1||f.channel>2||!finite(f.hz,20,20000)||!finite(f.gain,-24,24)||!finite(f.q,.1,30))return false;}return true;}
struct Biquad {
 double b0=1,b1=0,b2=0,a1=0,a2=0,z1=0,z2=0;
 void configure(const Filter& f,double rate){double w=2*3.14159265358979323846*f.hz/rate,c=cos(w),s=sin(w),alpha=s/(2*f.q),A=pow(10,f.gain/40),t=2*sqrt(A)*alpha,a0=1;
  switch(f.type){case 0:b0=1+alpha*A;b1=-2*c;b2=1-alpha*A;a0=1+alpha/A;a1=-2*c;a2=1-alpha/A;break;
   case 1:b0=A*((A+1)-(A-1)*c+t);b1=2*A*((A-1)-(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)+(A-1)*c+t;a1=-2*((A-1)+(A+1)*c);a2=(A+1)+(A-1)*c-t;break;
   case 2:b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)+(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)-(A+1)*c);a2=(A+1)-(A-1)*c-t;break;
   case 3:b0=(1-c)/2;b1=1-c;b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case 4:b0=(1+c)/2;b1=-(1+c);b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case 5:b0=1;b1=-2*c;b2=1;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case 6:b0=1-alpha;b1=-2*c;b2=1+alpha;a0=1+alpha;a1=-2*c;a2=1-alpha;break;}
  b0/=a0;b1/=a0;b2/=a0;a1/=a0;a2/=a0;z1=z2=0;
 }
 double process(double x){double y=b0*x+z1;z1=b1*x-a1*y+z2;z2=b2*x-a2*y;return y;}
};
struct Bank {
 Biquad left[32],right[32];int nl=0,nr=0,dl=0,dr=0,index=0;std::vector<double> delayL,delayR;
 double target=1,gain=1,lg=1,rg=1,limit=1,ceiling=1,fade=0,release=0;bool protection=false,active=false;uint64_t clipped=0,protectedFrames=0;double l=0,r=0;
 // rack state (镜像 Dsp.cs StereoProcessor)
 bool dcBlock=false;double dcA=0,dcXL=0,dcYL=0,dcXR=0,dcYR=0;
 double mLL=1,mLR=0,mRL=0,mRR=1,width=1;
 bool comp=false;double compThreshold=0,compRatio=1,compKnee=0,compMakeup=0,compAttack=0,compRelease=0,compEnv=1;
 void allocate(int rate){delayL.resize(rate/10+1);delayR.resize(delayL.size());fade=1-exp(-1/(rate*.005));release=1-exp(-1/(rate*.1));}
 void configure(const Config& c,int rate){nl=nr=0;index=0;std::fill(delayL.begin(),delayL.end(),0);std::fill(delayR.begin(),delayR.end(),0);active=c.active!=0;clipped=protectedFrames=0;limit=1;
  if(active&&c.enabled)for(uint32_t n=0;n<c.count;n++){auto& f=c.filters[n];if(!f.enabled||f.hz>=rate/2.0)continue;if(f.channel!=2)left[nl++].configure(f,rate);if(f.channel!=1)right[nr++].configure(f,rate);}
  target=active?pow(10,((c.enabled?c.preamp:0)+c.output)/20):1;gain=0;lg=active?((c.flags&2)?-1:1)*std::min(1.0,1-c.balance):1;rg=active?((c.flags&4)?-1:1)*std::min(1.0,1+c.balance):1;dl=active?int(round(c.delayL*rate/1000)):0;dr=active?int(round(c.delayR*rate/1000)):0;protection=active&&(c.flags&1);ceiling=pow(10,c.ceiling/20);
  dcBlock=active&&(c.flags&8);dcA=exp(-2*3.14159265358979323846*20/rate);dcXL=dcYL=dcXR=dcYR=0;
  mLL=c.mLL;mLR=c.mLR;mRL=c.mRL;mRR=c.mRR;width=c.width;if(mLL==0&&mLR==0&&mRL==0&&mRR==0&&width==0){mLL=mRR=1;width=1;}
  comp=active&&(c.flags&16);compThreshold=c.compThreshold;compRatio=c.compRatio;compKnee=c.compKnee;compMakeup=c.compMakeup;
  compAttack=1-exp(-1/(rate*std::max(c.compAttackMs,.1)/1000));compRelease=1-exp(-1/(rate*std::max(c.compReleaseMs,5.)/1000));compEnv=1;
 }
 void process(double inL,double inR){if(!active){l=inL;r=inR;return;}
  // 1) 直流滤波（一阶 20Hz 高通，镜像 Dsp.cs）
  if(dcBlock){dcYL=inL-dcXL+dcA*dcYL;dcXL=inL;inL=dcYL;dcYR=inR-dcXR+dcA*dcYR;dcXR=inR;inR=dcYR;}
  // 2) EQ
  for(int n=0;n<nl;n++)inL=left[n].process(inL);for(int n=0;n<nr;n++)inR=right[n].process(inR);
  // 3) 矩阵 + 立体声宽度（M/S）
  if(mLL!=1||mLR!=0||mRL!=0||mRR!=1||width!=1){double nl2=inL*mLL+inR*mLR,nr2=inL*mRL+inR*mRR;if(width!=1){double mid=(nl2+nr2)*.5,side=(nl2-nr2)*.5*width;nl2=mid+side;nr2=mid-side;}inL=nl2;inR=nr2;}
  // 4) 增益/平衡/反相 + 延时
  gain+=(target-gain)*fade;delayL[index]=inL*gain*lg;delayR[index]=inR*gain*rg;int size=int(delayL.size());l=delayL[(index-dl+size)%size];r=delayR[(index-dr+size)%size];index=(index+1)%size;
  // 5) 压缩器（立体声联动，软拐点，镜像 Dsp.cs）
  if(comp){double det=std::max(abs(l),abs(r));double over=20*log10(std::max(det,1e-9))-compThreshold;
   double grDb=over>compKnee/2?over*(1/compRatio-1):(over>-compKnee/2&&compKnee>0?(1/compRatio-1)*(over+compKnee/2)*(over+compKnee/2)/(2*compKnee):0);
   double t=pow(10,(grDb+compMakeup)/20);compEnv+=(t-compEnv)*(t<compEnv?compAttack:compRelease);l*=compEnv;r*=compEnv;}
  // 6) 峰值保护（sample-peak）
  if(protection){double peak=std::max(abs(l),abs(r)),desired=peak>ceiling?ceiling/peak:1;limit=desired<limit?desired:std::min(desired,limit+(1-limit)*release);if(limit<.999999)protectedFrames++;l*=limit;r*=limit;}
  // Preserve float headroom. Physical PCM conversion remains the Windows endpoint's job.
  if(abs(l)>1||abs(r)>1)clipped++;
 }
};
}
