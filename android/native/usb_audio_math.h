#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <stdexcept>

namespace usb_audio {
inline int service_ticks(int speed,int interval){
 // libusb speed 2=full, 3=high. SuperSpeed requires companion descriptors.
 if(speed!=2&&speed!=3)throw std::runtime_error("Only full/high-speed UAC is supported");
 if(interval<1||interval>4||(speed==2&&interval!=1))throw std::runtime_error("Unsupported isochronous interval");
 return 1<<(interval-1);
}
inline int packets_per_transfer(int speed,int interval){int ticks=service_ticks(speed,interval);return speed==3?std::max(1,8/ticks):1;}
struct FaultBudget {
 uint64_t windowUs=0;int consecutive=0,inWindow=0;
 void healthy(){consecutive=0;}
 bool recover(uint64_t nowUs){
  if(!windowUs||nowUs-windowUs>=1000000){windowUs=nowUs;inWindow=0;}
  return ++consecutive<=3&&++inWindow<=8;
 }
};
struct PacketClock {
 double samplesPerTick,fraction=0;int ticks,maxFrames;
 PacketClock(int rate,int baseTicks,int intervalTicks,int capacity):samplesPerTick(double(rate)/baseTicks),ticks(intervalTicks),maxFrames(capacity){
  if(rate<=0||baseTicks<=0||capacity<int(std::ceil(samplesPerTick*ticks)))throw std::runtime_error("Endpoint packet capacity is insufficient");
 }
 int next(){
  fraction+=samplesPerTick*ticks;int frames=int(std::floor(fraction));fraction-=frames;
  if(frames<0||frames>maxFrames)throw std::runtime_error("Feedback exceeds endpoint capacity");
  return frames;
 }
 bool feedback(const uint8_t* data,int length,double nominal,bool highSpeed=false){
  if(length!=3&&length!=4)return false;
  uint32_t raw=0;for(int i=0;i<length;i++)raw|=uint32_t(data[i])<<(8*i);
  double value=raw/(length==3?16384.0:65536.0);
  // A few HS devices send the FS 10.14 form per frame instead of per microframe.
  if(highSpeed&&length==3&&std::abs(value/8-nominal)<std::abs(value-nominal))value/=8;
  if(!std::isfinite(value)||value<nominal*.98||value>nominal*1.02)return false;
  samplesPerTick+=(value-samplesPerTick)/16;return true;
 }
};
inline void pcm(double sample,int bytes,int validBits,uint8_t* target){
 if(!std::isfinite(sample))sample=0;
 double scale=double(uint64_t(1)<<(validBits-1)),value=std::round(std::clamp(double(sample),-1.0,1.0)*scale);
 int64_t signedValue=int64_t(std::clamp(value,-scale,scale-1));
 uint32_t word=uint32_t(signedValue)<<(bytes*8-validBits);
 for(int i=0;i<bytes;i++)target[i]=uint8_t(word>>(i*8));
}
inline void dop(const uint8_t* payload,int bytes,uint8_t marker,uint8_t* target){
 for(int channel=0;channel<2;channel++){
  auto* output=target+channel*bytes;int shift=bytes==4?1:0;if(shift)output[0]=0;
  output[shift]=payload[channel*2];output[shift+1]=payload[channel*2+1];output[shift+2]=marker;
 }
}
}
