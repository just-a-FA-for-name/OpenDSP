// Host fault injection into the production callback. The USB transport is fake.
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <future>
#include <iostream>
#include <map>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>
#include "../android/native/usb_audio_math.h"
using jlong=int64_t;
#define LIBUSB_CALL
enum {LIBUSB_TRANSFER_COMPLETED,LIBUSB_TRANSFER_ERROR,LIBUSB_TRANSFER_TIMED_OUT,LIBUSB_TRANSFER_CANCELLED,LIBUSB_TRANSFER_STALL,LIBUSB_TRANSFER_NO_DEVICE,LIBUSB_TRANSFER_OVERFLOW};
enum {LIBUSB_SPEED_HIGH=3,LIBUSB_ERROR_INTERRUPTED=-10,PRIO_PROCESS=0};
struct timeval {long tv_sec,tv_usec;};
int setpriority(int,int,int){return 0;}int pthread_self(){return 0;}int pthread_setname_np(int,const char*){return 0;}
struct libusb_iso_packet_descriptor {unsigned int length=0,actual_length=0;int status=0;};
struct libusb_transfer {int endpoint=0,num_iso_packets=0,length=0,status=0;void* user_data=nullptr;unsigned char* buffer=nullptr;libusb_iso_packet_descriptor iso_packet_desc[8];};
struct Connection {void* handle=nullptr;void* ctx=nullptr;explicit Connection(int){}};
struct Format {int control=0,interfaceId=2,alt=3,protocol=0x20,clock=10,channels=2,bytes=4,bits=32,endpoint=3,feedback=132,feedbackBytes=4,interval=1,maxPacket=400,sync=1;};
uint32_t deviceRate=48000;int interrupts=0;
void require(bool yes,const std::string& text){if(!yes)throw std::runtime_error(text);}
void usbCheck(int result,const char* text){require(result==0,text);}
uint32_t le(const unsigned char* p,int n){uint32_t result=0;for(int i=0;i<n;i++)result|=uint32_t(p[i])<<(i*8);return result;}
std::vector<Format> formats(Connection&){return {Format{}};}bool supports(const Format&,int){return true;}
void* libusb_get_device(void*){return nullptr;}int libusb_get_device_speed(void*){return 3;}
int libusb_set_auto_detach_kernel_driver(void*,int value){require(value==0,"duplicate detach regression");return 0;}
int libusb_claim_interface(void*,int){return 0;}int libusb_release_interface(void*,int){return 0;}int libusb_attach_kernel_driver(void*,int){return 0;}
int libusb_set_interface_alt_setting(void*,int,int){return 0;}
int libusb_control_transfer(void*,int type,int,int value,int,unsigned char* data,int count,int){
 if(type==0x21){deviceRate=le(data,count);return count;}
 if(value==0x200){data[0]=1;return 1;}
 for(int i=0;i<count;i++)data[i]=uint8_t(deviceRate>>(i*8));return count;
}
libusb_transfer* libusb_alloc_transfer(int count){require(count<=8,"mock packet count");return new libusb_transfer;}
void libusb_free_transfer(libusb_transfer* p){delete p;}
void libusb_fill_iso_transfer(libusb_transfer* p,void*,int endpoint,unsigned char* buffer,int length,int count,void(*)(libusb_transfer*),void* user,int){
 p->endpoint=endpoint;p->buffer=buffer;p->length=length;p->num_iso_packets=count;p->user_data=user;
}
int libusb_submit_transfer(libusb_transfer*){return 0;}int libusb_cancel_transfer(libusb_transfer*){return 0;}
int libusb_handle_events_timeout_completed(void*,timeval*,void*){return 0;}
const char* libusb_error_name(int){return "fake";}
void libusb_interrupt_event_handler(void*){interrupts++;}
#include "../android/native/usb_stream.h"
void successfulPackets(Stream::Slot& slot){
 slot.transfer->status=LIBUSB_TRANSFER_COMPLETED;
 for(int i=0;i<slot.transfer->num_iso_packets;i++){auto& p=slot.transfer->iso_packet_desc[i];p.status=LIBUSB_TRANSFER_COMPLETED;p.actual_length=p.length;}
}
void source(Stream& stream){std::vector<uint8_t> bytes(30000*stream.inputFrame);stream.push(bytes.data(),bytes.size());}
void shortPacket(Stream::Slot& slot){successfulPackets(slot);slot.transfer->iso_packet_desc[2].actual_length=0;}
int main(){
 try{
  {
   Stream s(1,384000,32,false);source(s);auto& slot=*s.slots[0];s.submit(slot);shortPacket(slot);Stream::callback(slot.transfer);
   require(!s.stopping&&s.errors==1&&s.recoveredTransfers==1,"single PCM error must not end session");
   require(s.completed==336&&s.lostAudio==48,"retire only lost packet's source frames");
   require(s.status().find("expected=384 actual=0")!=std::string::npos,"retain exact packet fault");
   successfulPackets(slot);Stream::callback(slot.transfer);require(s.completed==720&&s.lostAudio==48,"healthy callback resumes fresh data");
  }
  {
   Stream s(1,384000,32,false);source(s);auto& slot=*s.slots[0];s.submit(slot);
   for(int i=0;i<4;i++){shortPacket(slot);Stream::callback(slot.transfer);}
   require(s.stopping&&s.recoveredTransfers==3&&s.errors==4,"bounded consecutive recovery");
  }
  {
   Stream s(1,384000,32,false);source(s);auto& slot=*s.slots[0];s.submit(slot);slot.transfer->status=LIBUSB_TRANSFER_NO_DEVICE;Stream::callback(slot.transfer);
   require(s.stopping&&s.recoveredTransfers==0,"device removal is fatal");
  }
  {
   Stream s(1,384000,32,false);auto& slot=*s.slots.back();s.submit(slot);slot.transfer->status=LIBUSB_TRANSFER_ERROR;Stream::callback(slot.transfer);
   require(!s.stopping&&s.feedbackErrors==1&&s.errors==0,"feedback loss is not audio endpoint loss");
   successfulPackets(slot);slot.transfer->iso_packet_desc[0].actual_length=4;uint8_t feedback[4]={0,0,48,0};std::memcpy(slot.buffer.data(),feedback,4);Stream::callback(slot.transfer);
   require(s.feedbackCount==1,"feedback recovers");
  }
  {
   Stream s(1,176400,32,true);source(s);auto& slot=*s.slots[0];s.submit(slot);shortPacket(slot);Stream::callback(slot.transfer);
   require(s.stopping&&s.recoveredTransfers==0,"DoP must not conceal audio packet errors");
  }
  {
   Stream s(1,384000,32,false);std::vector<uint8_t> bytes(s.ring.size());s.push(bytes.data(),bytes.size());
   auto blocked=std::async(std::launch::async,[&]{try{s.push(bytes.data(),8);return false;}catch(const std::exception&){return true;}});
   s.requestStop();require(blocked.wait_for(std::chrono::milliseconds(100))==std::future_status::ready&&blocked.get(),"cancel wakes blocked writer");
   require(interrupts>0,"cancel wakes libusb event handler");
  }
  std::cout<<"PASS: production callback PCM loss accounting, bounded recovery, feedback loss, no-device/DoP fail-fast, cancel wakeup\n";
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
