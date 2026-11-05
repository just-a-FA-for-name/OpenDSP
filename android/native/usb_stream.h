// Included inside usb_output.cpp's private namespace after descriptor discovery.
using Clock=std::chrono::steady_clock;
struct Stream:std::enable_shared_from_this<Stream>{
 static constexpr int dataTransfers=64;
 static constexpr int startupMs=160,ringTargetMs=96;
 static uint64_t nowNs(){return uint64_t(std::chrono::duration_cast<std::chrono::nanoseconds>(Clock::now().time_since_epoch()).count());}
 struct Slot {Stream* owner=nullptr;libusb_transfer* transfer=nullptr;std::vector<uint8_t> buffer;bool feedback=false,submitted=false;uint64_t audio=0,wire=0;std::array<uint32_t,8> packetAudio{};};
 Connection connection;Format format;int rate,bits,speed,baseTicks,ticks,packetCount,wireFrame,inputFrame;
 bool dopMode,claimedControl=false,claimedStream=false;uint32_t oldRate=0;std::unique_ptr<usb_audio::PacketClock> packetClock;
 std::vector<uint8_t> ring;size_t readAt=0,writeAt=0,queued=0;std::mutex mutex,producer;std::condition_variable changed;
 std::vector<std::unique_ptr<Slot>> slots;std::thread worker;std::atomic<bool> stopping{false},finished{false},eof{false};
 std::atomic<uint64_t> accepted{0},completed{0},packets{0},underruns{0},errors{0},feedbackCount{0};
 std::atomic<uint64_t> startedNs{0},lastDataNs{0},maxDataGapUs{0},lateDataCallbacks{0},feedbackRejected{0},wireCompleted{0},pcmClippedSamples{0},pcmInvalidSamples{0};
 std::atomic<double> feedbackRateHz{0};uint64_t queueBudgetUs=0;
 std::atomic<uint64_t> lostAudio{0},lostWire{0},feedbackErrors{0},recoveredTransfers{0},stopRequestedNs{0},ioStoppedNs{0};
 usb_audio::FaultBudget faultBudget;std::string lastTransferError;
 int pending=0;uint64_t wireSequence=0;std::string failure;Clock::time_point lastFeedback,drained;
 std::array<double,8192> pcmScratch{};std::array<uint8_t,32768> byteScratch{};
 Stream(int fd,int requestedRate,int requestedBits,bool dop):connection(fd),rate(requestedRate),bits(requestedBits),dopMode(dop){
  require(rate>=8000&&rate<=768000,"Unsupported requested rate");require(bits==16||bits==24||bits==32,"Unsupported PCM width");
  require(!dop||(rate==176400||rate==352800),"DoP carrier must be 176400 or 352800");
  speed=libusb_get_device_speed(libusb_get_device(connection.handle));baseTicks=speed==LIBUSB_SPEED_HIGH?8000:1000;
  bool selected=false;for(const auto& candidate:formats(connection))if(supports(candidate,rate)&&candidate.bits==bits&&candidate.bytes==bits/8){
   if(dop&&candidate.bits<24)continue;format=candidate;selected=true;break;
  }
  require(selected,"Requested rate/valid bits/container not advertised; no format fallback");
  require(format.sync==2||format.sync==3||(format.sync==1&&format.feedback),"Unsupported/implicit feedback transport");
  if(format.feedback)require(format.feedbackBytes==3||format.feedbackBytes==4,"Unsupported feedback packet format");
  ticks=usb_audio::service_ticks(speed,format.interval);packetCount=usb_audio::packets_per_transfer(speed,format.interval);
  queueBudgetUs=uint64_t(dataTransfers)*packetCount*ticks*1000000/baseTicks;feedbackRateHz=rate;
  wireFrame=format.channels*format.bytes;inputFrame=dop?4:wireFrame;
  packetClock=std::make_unique<usb_audio::PacketClock>(rate,baseTicks,ticks,format.maxPacket/wireFrame);ring.resize(size_t(rate/2)*inputFrame);
  try{
   // UsbDeviceConnection.claimInterface(force=true) already detached and claimed
   // these interfaces on this same open file description (retained with dup).
   // DISCONNECT_CLAIM_EXCEPT_DRIVER("usbfs") would reject our own existing claim.
   usbCheck(libusb_set_auto_detach_kernel_driver(connection.handle,0),"disable duplicate kernel detach");
   usbCheck(libusb_claim_interface(connection.handle,format.control),"claim control");claimedControl=true;
   usbCheck(libusb_claim_interface(connection.handle,format.interfaceId),"claim output");claimedStream=true;
   usbCheck(libusb_set_interface_alt_setting(connection.handle,format.interfaceId,0),"idle audio alternate");
   oldRate=readRate();setRate(rate);require(readRate()==uint32_t(rate),"DAC rate readback differs from request");
   usbCheck(libusb_set_interface_alt_setting(connection.handle,format.interfaceId,format.alt),"select audio alternate");
   if(format.protocol==0x20){
    unsigned char valid=0;int result=libusb_control_transfer(connection.handle,0xa1,1,0x200,(format.clock<<8)|format.control,&valid,1,500);
    if(result==1&&valid==0)throw std::runtime_error("DAC clock is not valid");
   }
   for(int i=0;i<dataTransfers;i++)allocate(false);if(format.feedback)for(int i=0;i<2;i++)allocate(true);
  }catch(...){release();throw;}
 }
 uint32_t readRate(){
  unsigned char data[4]{};int count=format.protocol==0?3:4;
  int n=libusb_control_transfer(connection.handle,format.protocol==0?0xa2:0xa1,format.protocol==0?0x81:1,0x100,format.protocol==0?format.endpoint:(format.clock<<8)|format.control,data,count,600);
  require(n==count,"Cannot read DAC sample rate");return le(data,count);
 }
 void setRate(uint32_t value){
  unsigned char data[4];for(int i=0;i<4;i++)data[i]=uint8_t(value>>(8*i));int count=format.protocol==0?3:4;
  int n=libusb_control_transfer(connection.handle,format.protocol==0?0x22:0x21,1,0x100,format.protocol==0?format.endpoint:(format.clock<<8)|format.control,data,count,600);
  require(n==count,"DAC rejected sample-rate control");
 }
 void release(){
  for(auto& slot:slots)if(slot->transfer){libusb_free_transfer(slot->transfer);slot->transfer=nullptr;}
  if(claimedStream){libusb_set_interface_alt_setting(connection.handle,format.interfaceId,0);if(oldRate)try{if(readRate()==uint32_t(rate))setRate(oldRate);}catch(...){}
   libusb_release_interface(connection.handle,format.interfaceId);libusb_attach_kernel_driver(connection.handle,format.interfaceId);claimedStream=false;}
  if(claimedControl){libusb_release_interface(connection.handle,format.control);libusb_attach_kernel_driver(connection.handle,format.control);claimedControl=false;}
 }
 ~Stream(){if(worker.joinable())worker.join();release();}
 void allocate(bool feedback){
  auto slot=std::make_unique<Slot>();slot->owner=this;slot->feedback=feedback;int count=feedback?1:packetCount;
  slot->transfer=libusb_alloc_transfer(count);require(slot->transfer!=nullptr,"Cannot allocate USB transfers");
  slot->buffer.resize(feedback?format.feedbackBytes:format.maxPacket*count);
  libusb_fill_iso_transfer(slot->transfer,connection.handle,feedback?format.feedback:format.endpoint,slot->buffer.data(),int(slot->buffer.size()),count,callback,slot.get(),0);
  if(feedback)slot->transfer->iso_packet_desc[0].length=format.feedbackBytes;slots.push_back(std::move(slot));
 }
 void fail(const std::string& message){{std::lock_guard<std::mutex> lock(mutex);if(failure.empty())failure=message;}stopping=true;changed.notify_all();}
 static void LIBUSB_CALL callback(libusb_transfer* transfer){
  auto& slot=*static_cast<Slot*>(transfer->user_data);Stream& s=*slot.owner;slot.submitted=false;s.pending--;
  if(s.stopping)return;
  bool bad=false,recoverable=true;uint64_t delivered=0,wire=0,verifiedPackets=0;std::string detail;
  if(transfer->status==LIBUSB_TRANSFER_COMPLETED){
   for(int i=0;i<transfer->num_iso_packets;i++){
    auto& packet=transfer->iso_packet_desc[i];bool valid=packet.status==LIBUSB_TRANSFER_COMPLETED&&(slot.feedback?packet.actual_length>0:packet.actual_length==packet.length);
    if(!valid){
     bad=true;
     if(packet.status!=LIBUSB_TRANSFER_COMPLETED&&packet.status!=LIBUSB_TRANSFER_ERROR&&packet.status!=LIBUSB_TRANSFER_TIMED_OUT)recoverable=false;
     if(detail.empty())detail="endpoint="+std::to_string(transfer->endpoint)+" packet="+std::to_string(i)+" status="+std::to_string(packet.status)+" expected="+std::to_string(packet.length)+" actual="+std::to_string(packet.actual_length);
    }else if(!slot.feedback){delivered+=slot.packetAudio[i];wire+=packet.length/s.wireFrame;verifiedPackets++;}
   }
  }else{
   bad=true;recoverable=transfer->status==LIBUSB_TRANSFER_ERROR||transfer->status==LIBUSB_TRANSFER_TIMED_OUT;
   detail="endpoint="+std::to_string(transfer->endpoint)+" transfer_status="+std::to_string(transfer->status);
  }
  if(slot.feedback){
   if(bad){s.feedbackErrors++;{std::lock_guard<std::mutex> lock(s.mutex);s.lastTransferError=detail;}if(!recoverable){s.errors++;s.fail("Feedback transfer: "+detail);}}
   else if(s.packetClock->feedback(slot.buffer.data(),transfer->iso_packet_desc[0].actual_length,double(s.rate)/s.baseTicks,s.baseTicks==8000)){s.feedbackCount++;s.feedbackRateHz=s.packetClock->samplesPerTick*s.baseTicks;s.lastFeedback=Clock::now();}else s.feedbackRejected++;
  }else{
   s.completed+=delivered;s.wireCompleted+=wire;s.packets+=verifiedPackets;
   if(bad){
    s.errors++;s.lostAudio+=slot.audio-delivered;s.lostWire+=slot.wire-wire;{std::lock_guard<std::mutex> lock(s.mutex);s.lastTransferError=detail;}
    if(!recoverable||s.dopMode||!s.faultBudget.recover(nowNs()/1000))s.fail("USB output failure: "+detail);
    else s.recoveredTransfers++;
   }else{
    s.faultBudget.healthy();
    uint64_t now=nowNs(),previous=s.lastDataNs.exchange(now);
    if(previous){uint64_t gap=(now-previous)/1000;if(gap>s.maxDataGapUs.load())s.maxDataGapUs=gap;if(gap>s.queueBudgetUs)s.lateDataCallbacks++;}
   }
   s.changed.notify_all();
  }
  if(!s.stopping)try{s.submit(slot);}catch(const std::exception& ex){s.errors++;s.fail(ex.what());}
 }
 void submit(Slot& slot){
  if(!slot.feedback){
   size_t offset=0;slot.audio=0;slot.wire=0;slot.packetAudio.fill(0);std::unique_lock<std::mutex> lock(mutex);
   for(int p=0;p<packetCount;p++){
    int frames=packetClock->next();slot.wire+=frames;slot.transfer->iso_packet_desc[p].length=frames*wireFrame;
    for(int f=0;f<frames;f++){
     uint8_t payload[8]{};bool data=queued>=size_t(inputFrame);
     if(data){std::memcpy(payload,ring.data()+readAt,inputFrame);readAt+=inputFrame;if(readAt==ring.size())readAt=0;queued-=inputFrame;slot.audio++;slot.packetAudio[p]++;}
     else{if(dopMode)std::memset(payload,0x69,4);if(!eof)underruns++;}
     if(dopMode)usb_audio::dop(payload,format.bytes,(wireSequence++&1)?0xfa:0x05,slot.buffer.data()+offset);
     else std::memcpy(slot.buffer.data()+offset,payload,wireFrame);offset+=wireFrame;
    }
   }
   slot.transfer->length=int(offset);changed.notify_all();
  }
  usbCheck(libusb_submit_transfer(slot.transfer),"submit isochronous");slot.submitted=true;pending++;
 }
 void launch(){auto self=shared_from_this();worker=std::thread([self]{self->run();});}
 void run(){
  pthread_setname_np(pthread_self(),"OpenDSP-USB-IO");
  setpriority(PRIO_PROCESS,0,-19);bool cancelled=false;
  try{{std::unique_lock<std::mutex> lock(mutex);changed.wait(lock,[&]{return stopping||eof||queued>=size_t(rate)*startupMs/1000*inputFrame;});}
   lastFeedback=Clock::now();if(!stopping){startedNs=nowNs();for(auto& slot:slots)submit(*slot);}
  }catch(const std::exception& ex){fail(ex.what());}
  while(pending>0){
   if(stopping&&!cancelled){cancelled=true;for(auto& slot:slots)if(slot->submitted)libusb_cancel_transfer(slot->transfer);}
   timeval wait{0,20000};int result=libusb_handle_events_timeout_completed(connection.ctx,&wait,nullptr);
   if(result<0&&result!=LIBUSB_ERROR_INTERRUPTED)fail(std::string("USB events: ")+libusb_error_name(result));
   auto now=Clock::now();
   if(!stopping&&format.sync==1&&now-lastFeedback>std::chrono::milliseconds(1500))fail("Asynchronous DAC feedback missing/stale");
   if(eof&&completed.load()+lostAudio.load()>=accepted.load()){
    if(drained==Clock::time_point{})drained=now;
    if(now-drained>std::chrono::milliseconds(40)){stopping=true;changed.notify_all();}
   }
  }
  ioStoppedNs=nowNs();finished=true;changed.notify_all();
 }
 void push(const uint8_t* data,size_t bytes){
  size_t done=0;std::unique_lock<std::mutex> lock(mutex);
  while(done<bytes){
   changed.wait(lock,[&]{return stopping||queued<ring.size();});
   if(stopping)throw std::runtime_error(failure.empty()?"USB stream stopped":failure);
   size_t count=std::min({bytes-done,ring.size()-queued,ring.size()-writeAt});
   std::memcpy(ring.data()+writeAt,data+done,count);writeAt=(writeAt+count)%ring.size();queued+=count;done+=count;changed.notify_all();
  }
  accepted+=bytes/inputFrame;
 }
 void requestStop(){uint64_t expected=0;stopRequestedNs.compare_exchange_strong(expected,nowNs());stopping=true;changed.notify_all();libusb_interrupt_event_handler(connection.ctx);}
 std::string status(){
  std::lock_guard<std::mutex> lock(mutex);std::string escaped;for(char ch:failure){if(ch=='\"'||ch=='\\')escaped+='\\';escaped+=ch;}
  std::ostringstream out;out<<"{\"usb_claimed\":true,\"running\":"<<(!stopping?"true":"false")<<",\"finished\":"<<(finished?"true":"false")
   <<",\"rate\":"<<rate<<",\"valid_bits\":"<<format.bits<<",\"container_bits\":"<<format.bytes*8<<",\"dop\":"<<(dopMode?"true":"false")
   <<",\"accepted_frames\":"<<accepted<<",\"completed_frames\":"<<completed<<",\"packets\":"<<packets<<",\"underrun_frames\":"<<underruns
   <<",\"errors\":"<<errors<<",\"feedback_packets\":"<<feedbackCount<<",\"queued_frames\":"<<queued/inputFrame
   <<",\"usb_queue_budget_us\":"<<queueBudgetUs<<",\"startup_buffer_ms\":"<<startupMs<<",\"ring_target_ms\":"<<ringTargetMs<<",\"max_data_callback_gap_us\":"<<maxDataGapUs<<",\"callbacks_over_queue_budget\":"<<lateDataCallbacks
   <<",\"feedback_rejected\":"<<feedbackRejected<<",\"feedback_rate_hz\":"<<feedbackRateHz.load()<<",\"wire_completed_frames\":"<<wireCompleted
   <<",\"pcm_clipped_samples\":"<<pcmClippedSamples<<",\"pcm_invalid_samples\":"<<pcmInvalidSamples
   <<",\"lost_audio_frames\":"<<lostAudio<<",\"lost_wire_frames\":"<<lostWire<<",\"feedback_transfer_errors\":"<<feedbackErrors<<",\"recovered_output_transfers\":"<<recoveredTransfers
   <<",\"stop_to_io_stopped_us\":"<<(stopRequestedNs.load()&&ioStoppedNs.load()>stopRequestedNs.load()?(ioStoppedNs.load()-stopRequestedNs.load())/1000:0)
   <<",\"stream_elapsed_us\":"<<(startedNs.load()?(nowNs()-startedNs.load())/1000:0)<<",\"last_transfer_error\":\"";
  for(char ch:lastTransferError){if(ch=='\"'||ch=='\\')out<<'\\';out<<ch;}
  out<<"\",\"error\":\""<<escaped<<"\"}";return out.str();
 }
};
std::mutex sessionsMutex;std::map<jlong,std::shared_ptr<Stream>> sessions;jlong nextSession=1;
std::shared_ptr<Stream> session(jlong id){std::lock_guard<std::mutex> lock(sessionsMutex);auto found=sessions.find(id);require(found!=sessions.end(),"USB stream no longer exists");return found->second;}
