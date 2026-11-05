#pragma once
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <stdexcept>
#if defined(__aarch64__)
#include <arm_neon.h>
#endif
namespace openeq {
/** Same interpolated 64-tap converter as Java, with a stereo NEON inner loop. */
class SincResampler {
 std::array<std::array<double,64>,257> kernel{};
 std::array<std::array<double,64>,256> delta{};
 std::array<double,16384> ring{};
 int inputRate,outputRate;double ratio,step,position=0,ppm=0;int64_t loaded=0,total=-1;
 bool adaptive;
 public:
 SincResampler(int input,int output,bool clock):inputRate(input),outputRate(output),ratio(double(input)/output),step(ratio),adaptive(clock){
  if(input<8000||input>768000||output<8000||output>768000)throw std::runtime_error("Invalid SRC rate");
  constexpr double pi=3.14159265358979323846;double cutoff=std::min(1.,output/double(input))*.94;
  for(int p=0;p<=256;p++){double sum=0,fraction=p/256.;
   for(int t=0;t<64;t++){double x=t-31-fraction,window=.42+.5*std::cos(pi*x/32)+.08*std::cos(2*pi*x/32);double value=std::abs(x)<1e-12?cutoff:std::sin(pi*cutoff*x)/(pi*x);sum+=(kernel[p][t]=value*window);}
   for(double& value:kernel[p])value/=sum;
  }
  for(int p=0;p<256;p++)for(int t=0;t<64;t++)delta[p][t]=kernel[p+1][t]-kernel[p][t];
 }
 void clockPpm(double value){if(!std::isfinite(value))throw std::runtime_error("Invalid SRC clock");if(adaptive){ppm+=(std::clamp(value,-2000.,2000.)-ppm)/16;step=ratio*(1+ppm/1000000);}}
 void push(const double* input,int frames){
  if(total>=0||frames<0||frames>1024)throw std::runtime_error("Invalid SRC input");
  if(loaded+frames-int64_t(position)>8192-64)throw std::runtime_error("SRC input queue full");
  for(int i=0;i<frames;i++){int at=int(loaded++&8191)*2;ring[at]=std::isfinite(input[2*i])?input[2*i]:0;ring[at+1]=std::isfinite(input[2*i+1])?input[2*i+1]:0;}
 }
 void finish(){total=loaded;}
 int read(double* out,int frames){
  int done=0;
  while(done<frames){
   int64_t base=int64_t(position);if(total<0&&loaded<=base+32)break;if(total>=0&&position>=total)break;
   if(base>=31&&base-31<loaded-8192)throw std::runtime_error("SRC history lost");
   double fractional=(position-base)*256;int phase=std::min(255,int(fractional));double blend=fractional-phase;
   const auto& weights=kernel[phase];const auto& diff=delta[phase];double left=0,right=0;
   if(base>=31&&base+32<loaded){
    int at=int((base-31)&8191)*2;
#if defined(__aarch64__)
    float64x2_t sum=vdupq_n_f64(0);
    for(int t=0;t<64;t++){double weight=weights[t]+blend*diff[t];sum=vfmaq_n_f64(sum,vld1q_f64(ring.data()+at),weight);at=(at+2)&16383;}
    left=vgetq_lane_f64(sum,0);right=vgetq_lane_f64(sum,1);
#else
    for(int t=0;t<64;t++){double weight=weights[t]+blend*diff[t];left+=ring[at]*weight;right+=ring[at+1]*weight;at=(at+2)&16383;}
#endif
   }else for(int t=0;t<64;t++){int64_t index=base+t-31;if(index<0||index>=loaded)continue;int at=int(index&8191)*2;double weight=weights[t]+blend*diff[t];left+=ring[at]*weight;right+=ring[at+1]*weight;}
   out[done*2]=left;out[done*2+1]=right;done++;position+=step;
  }return done;
 }
};
}
