#include "../android/native/usb_audio_math.h"
#include <iostream>
#include <limits>
void check(bool yes,const char* message){if(!yes)throw std::runtime_error(message);}
int main(){
 try{
  usb_audio::FaultBudget burst;check(burst.recover(100)&&burst.recover(200)&&burst.recover(300)&&!burst.recover(400),"Bound consecutive PCM recovery");
  usb_audio::FaultBudget rateLimit;for(int i=0;i<8;i++){rateLimit.healthy();check(rateLimit.recover(1000+i*1000),"Recover isolated loss");}rateLimit.healthy();check(!rateLimit.recover(10000),"Bound aggregate loss");rateLimit.healthy();check(rateLimit.recover(1001000),"Fault window expires");
  check(usb_audio::packets_per_transfer(2,1)==1&&usb_audio::packets_per_transfer(3,1)==8&&usb_audio::packets_per_transfer(3,4)==1,"full/high-speed transfers each cover one millisecond");
  for(int rate:{44100,48000,88200,96000,176400,192000,352800,384000}){
   usb_audio::PacketClock clock(rate,8000,1,128);long total=0;for(int i=0;i<80000;i++)total+=clock.next();
   check(std::abs(total-rate*10L)<=1,"fractional packet rate drift");
  }
  usb_audio::PacketClock clock(384000,8000,1,49);uint8_t feedback[4]={0,0,48,0};check(clock.feedback(feedback,4,48,true),"HS16.16 feedback");
  uint8_t bad[4]={0,0,0,0};check(!clock.feedback(bad,4,48,true),"zero feedback rejected");
  uint8_t fs[3]={0,0,12};check(clock.feedback(fs,3,48,true),"HS10.14 feedback"); // 48*16384
  uint8_t frameForm[3]={0,0,96};check(clock.feedback(frameForm,3,48,true),"HS frame-unit feedback quirk");check(!clock.feedback(frameForm,3,48,false),"FS cannot use HS quirk");
  for(double ppm:{-1500.0,1500.0}){
   usb_audio::PacketClock drift(384000,8000,1,50);uint32_t raw=uint32_t(std::llround(48*(1+ppm/1e6)*65536));uint8_t value[4];for(int i=0;i<4;i++)value[i]=uint8_t(raw>>(i*8));
   for(int i=0;i<1024;i++)check(drift.feedback(value,4,48,true),"Feedback drift accepted");
   int64_t frames=0;for(int i=0;i<480000;i++)frames+=drift.next();
   check(std::abs(double(frames)-raw/65536.0*480000)<=1,"One-minute feedback packet totals");
  }
  usb_audio::PacketClock hour(384000,8000,1,50);int64_t longTotal=0;for(int i=0;i<8000*3600;i++)longTotal+=hour.next();check(longTotal==384000LL*3600,"One-hour packet accumulation");
  bool failed=false;try{usb_audio::PacketClock tiny(384000,8000,1,47);}catch(...){failed=true;}check(failed,"capacity rejection");
  uint8_t data[8]{};usb_audio::pcm(1,4,32,data);check(data[0]==255&&data[3]==127,"PCM32 positive full scale");
  usb_audio::pcm(-1,4,32,data);check(data[0]==0&&data[3]==128,"PCM32 negative full scale");
  usb_audio::pcm(1.0/2147483648.0,4,32,data);check(data[0]==1&&data[3]==0,"PCM32 low bit preserved");
  usb_audio::pcm(std::numeric_limits<double>::quiet_NaN(),3,24,data);check(data[0]==0&&data[1]==0&&data[2]==0,"NaN zero");
  uint8_t payload[4]={0x12,0x34,0x56,0x78};usb_audio::dop(payload,4,0xfa,data);check(data[0]==0&&data[1]==0x12&&data[2]==0x34&&data[3]==0xfa&&data[5]==0x56&&data[7]==0xfa,"DoP32 left alignment");
  usb_audio::dop(payload,3,0x05,data);check(data[0]==0x12&&data[2]==5&&data[3]==0x56&&data[5]==5,"DoP24 marker");
  std::cout<<"PASS: eight-rate packet scheduling, minute feedback drift, hour accumulation, feedback rejection, PCM32 and DoP24/32\n";return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
