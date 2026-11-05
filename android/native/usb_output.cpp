// Copyright (c) 2026 Fa. MIT; libusb is a separate LGPL-2.1-or-later library.
#include <jni.h>
#include <libusb.h>
#include <unistd.h>
#include <fcntl.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include <atomic>
#include <array>
#include <chrono>
#include <condition_variable>
#include <map>
#include <mutex>
#include <thread>
#include <sys/resource.h>
#include <pthread.h>
#include "usb_audio_math.h"
#include "sinc_resampler.h"

namespace {
void require(bool ok,const std::string& error){if(!ok)throw std::runtime_error(error);}
void usbCheck(int status,const char* operation){if(status<0)throw std::runtime_error(std::string(operation)+": "+libusb_error_name(status));}
struct Connection {
 libusb_context* ctx=nullptr;libusb_device_handle* handle=nullptr;int owned=-1;
 explicit Connection(int fd){
  owned=dup(fd);require(owned>=0,"Unable to retain USB descriptor");
  libusb_init_option option{};option.option=LIBUSB_OPTION_NO_DEVICE_DISCOVERY;
  int code=libusb_init_context(&ctx,&option,1);
  if(code<0){close(owned);owned=-1;usbCheck(code,"libusb_init");}
  code=libusb_wrap_sys_device(ctx,owned,&handle);if(code<0){libusb_exit(ctx);ctx=nullptr;close(owned);owned=-1;usbCheck(code,"wrap USB descriptor");}
 }
 ~Connection(){if(handle)libusb_close(handle);if(ctx)libusb_exit(ctx);if(owned>=0)close(owned);}
 Connection(const Connection&)=delete;
};
struct Range{uint32_t min,max,step;};
struct Format {
 int interfaceId=0,alt=0,protocol=0,control=0,clock=0,terminal=0,channels=0,bytes=0,bits=0,endpoint=0,feedback=0,feedbackBytes=0,interval=0,maxPacket=0,sync=0;bool pcm=false;std::vector<Range> rates;
};
uint32_t le(const unsigned char* p,int n){uint32_t v=0;for(int i=0;i<n;i++)v|=uint32_t(p[i])<<(i*8);return v;}
template<typename F>void descriptors(const unsigned char* data,int size,F f){for(int pos=0;pos+2<=size;){int length=data[pos];require(length>=2&&pos+length<=size,"Malformed USB descriptor");f(data+pos,length);pos+=length;}}
std::vector<Format> formats(Connection& c){
 libusb_config_descriptor* config=nullptr;usbCheck(libusb_get_active_config_descriptor(libusb_get_device(c.handle),&config),"USB configuration");std::unique_ptr<libusb_config_descriptor,decltype(&libusb_free_config_descriptor)> owner(config,libusb_free_config_descriptor);
 int control=0,controls=0;std::vector<std::pair<int,int>> terminalClocks;std::map<int,std::vector<int>> selectors;std::vector<Format> result;std::map<int,std::vector<Range>> cachedRates;
 for(int i=0;i<config->bNumInterfaces;i++)for(int j=0;j<config->interface[i].num_altsetting;j++){auto& a=config->interface[i].altsetting[j];if(a.bInterfaceClass!=1||a.bInterfaceSubClass!=1)continue;control=a.bInterfaceNumber;
  if(a.bAlternateSetting==0)controls++;
  if(a.bInterfaceProtocol==0x20)descriptors(a.extra,a.extra_length,[&](const unsigned char* d,int n){if(d[1]==0x24&&n>=8){if(d[2]==2)terminalClocks.push_back({d[3],d[7]});if(d[2]==3&&n>=9)terminalClocks.push_back({d[3],d[8]});if(d[2]==11&&n>=7+d[4])selectors[d[3]]=std::vector<int>(d+5,d+5+d[4]);}});
 }
 require(controls==1,"This build requires one USB audio control function");
 for(int i=0;i<config->bNumInterfaces;i++)for(int j=0;j<config->interface[i].num_altsetting;j++){auto& a=config->interface[i].altsetting[j];if(a.bInterfaceClass!=1||a.bInterfaceSubClass!=2||!a.bAlternateSetting)continue;
  Format f;f.interfaceId=a.bInterfaceNumber;f.alt=a.bAlternateSetting;f.protocol=a.bInterfaceProtocol;f.control=control;
  // UAC3, compressed formats, implicit feedback and non-stereo layouts are rejected.
  if(f.protocol!=0&&f.protocol!=0x20)continue;
  descriptors(a.extra,a.extra_length,[&](const unsigned char* d,int n){if(d[1]!=0x24||n<3)return;if(d[2]==1){if(f.protocol==0&&n>=7)f.pcm=le(d+5,2)==1;if(f.protocol==0x20&&n>=16){f.terminal=d[3];f.channels=d[10];f.pcm=d[5]==1&&(le(d+6,4)&1);}}
   if(d[2]!=2||n<4||d[3]!=1)return;
   if(f.protocol==0&&n>=8){f.channels=d[4];f.bytes=d[5];f.bits=d[6];int count=d[7];if(count==0&&n>=14)f.rates.push_back({le(d+8,3),le(d+11,3),1});else if(n>=8+count*3)for(int k=0;k<count;k++){auto r=le(d+8+k*3,3);f.rates.push_back({r,r,0});}}
   if(f.protocol==0x20&&n>=6){f.bytes=d[4];f.bits=d[5];}
  });
  for(int k=0;k<a.bNumEndpoints;k++){auto& e=a.endpoint[k];if((e.bmAttributes&3)!=1)continue;int usage=(e.bmAttributes>>4)&3;if(!(e.bEndpointAddress&0x80)&&usage==0){f.endpoint=e.bEndpointAddress;f.sync=(e.bmAttributes>>2)&3;f.interval=e.bInterval;f.maxPacket=(e.wMaxPacketSize&2047)*(1+((e.wMaxPacketSize>>11)&3));}else if((e.bEndpointAddress&0x80)&&usage==1){f.feedback=e.bEndpointAddress;f.feedbackBytes=e.wMaxPacketSize&2047;}}
  if(!f.pcm||f.channels!=2||f.endpoint==0||(f.bytes!=2&&f.bytes!=3&&f.bytes!=4)||f.bits>f.bytes*8||f.bits<16)continue;
  if(f.protocol==0x20){for(auto pair:terminalClocks)if(pair.first==f.terminal)f.clock=pair.second;if(!f.clock)continue;
   for(int depth=0;selectors.count(f.clock)&&depth<8;depth++){unsigned char pin=0;int got=libusb_control_transfer(c.handle,0xa1,1,0x100,(f.clock<<8)|f.control,&pin,1,500);if(got!=1||pin==0||pin>selectors[f.clock].size()){f.clock=0;break;}f.clock=selectors[f.clock][pin-1];}
   if(!f.clock||selectors.count(f.clock))continue;
   if(cachedRates.count(f.clock)){f.rates=cachedRates[f.clock];result.push_back(f);continue;}
   unsigned char header[2]{};int index=(f.clock<<8)|f.control;int n=libusb_control_transfer(c.handle,0xa1,2,0x100,index,header,2,500);if(n!=2)continue;uint32_t count=le(header,2);if(!count||count>128)continue;std::vector<unsigned char> range(2+count*12);n=libusb_control_transfer(c.handle,0xa1,2,0x100,index,range.data(),range.size(),500);if(n!=int(range.size()))continue;
   for(uint32_t k=0;k<count;k++){auto* d=range.data()+2+k*12;uint32_t lo=le(d,4),hi=le(d+4,4);if(lo>0&&hi>=lo)f.rates.push_back({lo,hi,le(d+8,4)});}
   cachedRates[f.clock]=f.rates;
  }
  if(!f.rates.empty())result.push_back(f);
 }
 return result;
}
bool supports(const Format& f,int rate){for(auto r:f.rates)if(uint32_t(rate)>=r.min&&uint32_t(rate)<=r.max&&(r.step==0||((rate-r.min)%r.step)==0))return true;return false;}
std::string probe(int fd){require(fd>=0,"USB descriptor is invalid");Connection c(fd);auto caps=formats(c);std::ostringstream out;out<<"{\"libusb\":\"1.0.30\",\"speed\":"<<libusb_get_device_speed(libusb_get_device(c.handle))<<",\"formats\":[";bool first=true;for(auto& f:caps){if(!first)out<<',';first=false;out<<"{\"interface\":"<<f.interfaceId<<",\"alternate\":"<<f.alt<<",\"uac\":"<<(f.protocol==0?1:2)<<",\"channels\":"<<f.channels<<",\"container_bits\":"<<f.bytes*8<<",\"valid_bits\":"<<f.bits<<",\"endpoint\":"<<f.endpoint<<",\"feedback\":"<<f.feedback<<",\"interval\":"<<f.interval<<",\"max_packet\":"<<f.maxPacket<<",\"rates\":[";bool rFirst=true;for(auto r:f.rates){if(!rFirst)out<<',';rFirst=false;out<<"{\"min\":"<<r.min<<",\"max\":"<<r.max<<",\"step\":"<<r.step<<'}';}out<<"]}";}out<<"]}";return out.str();}
#include "usb_stream.h"
void error(JNIEnv* env,const std::exception& ex){env->ThrowNew(env->FindClass("java/io/IOException"),ex.what());}
}
extern "C" JNIEXPORT jstring JNICALL Java_org_openeq_app_UsbNative_probe(JNIEnv* env,jclass,jint fd){try{auto report=probe(fd);return env->NewStringUTF(report.c_str());}catch(const std::exception& e){error(env,e);return nullptr;}}
extern "C" JNIEXPORT jstring JNICALL Java_org_openeq_app_UsbNative_version(JNIEnv* env,jclass){auto v=libusb_get_version();std::ostringstream s;s<<v->major<<'.'<<v->minor<<'.'<<v->micro;return env->NewStringUTF(s.str().c_str());}
#include "usb_stream_jni.h"
#include "sinc_jni.h"
