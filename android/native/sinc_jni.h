// Included after the USB backend's session helpers, inside its anonymous namespace.
struct SrcSession {openeq::SincResampler converter;std::mutex mutex;std::array<double,8192> buffer{};SrcSession(int in,int out,bool adaptive):converter(in,out,adaptive){}};
std::mutex srcRegistryMutex;std::map<jlong,std::shared_ptr<SrcSession>> converters;jlong nextConverter=1;
std::shared_ptr<SrcSession> converter(jlong id){std::lock_guard<std::mutex> lock(srcRegistryMutex);auto it=converters.find(id);require(it!=converters.end(),"SRC session closed");return it->second;}
extern "C" JNIEXPORT jlong JNICALL Java_org_openeq_app_UsbNative_srcOpen(JNIEnv* env,jclass,jint in,jint out,jboolean adaptive){
 try{auto value=std::make_shared<SrcSession>(in,out,adaptive);std::lock_guard<std::mutex> lock(srcRegistryMutex);jlong id=nextConverter++;converters[id]=value;return id;}catch(const std::exception& ex){error(env,ex);return 0;}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_srcPush(JNIEnv* env,jclass,jlong id,jdoubleArray input,jint frames){
 try{auto s=converter(id);require(frames>0&&frames<=1024&&env->GetArrayLength(input)>=frames*2,"Invalid SRC input");std::lock_guard<std::mutex> lock(s->mutex);env->GetDoubleArrayRegion(input,0,frames*2,s->buffer.data());if(!env->ExceptionCheck())s->converter.push(s->buffer.data(),frames);}catch(const std::exception& ex){error(env,ex);}
}
extern "C" JNIEXPORT jint JNICALL Java_org_openeq_app_UsbNative_srcRead(JNIEnv* env,jclass,jlong id,jdoubleArray output,jint frames){
 try{auto s=converter(id);require(frames>0&&frames<=4096&&env->GetArrayLength(output)>=frames*2,"Invalid SRC output");std::lock_guard<std::mutex> lock(s->mutex);int count=s->converter.read(s->buffer.data(),frames);env->SetDoubleArrayRegion(output,0,count*2,s->buffer.data());return count;}catch(const std::exception& ex){error(env,ex);return 0;}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_srcClock(JNIEnv* env,jclass,jlong id,jdouble ppm){try{auto s=converter(id);std::lock_guard<std::mutex> lock(s->mutex);s->converter.clockPpm(ppm);}catch(const std::exception& ex){error(env,ex);}}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_srcFinish(JNIEnv* env,jclass,jlong id){try{auto s=converter(id);std::lock_guard<std::mutex> lock(s->mutex);s->converter.finish();}catch(const std::exception& ex){error(env,ex);}}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_srcClose(JNIEnv*,jclass,jlong id){std::lock_guard<std::mutex> lock(srcRegistryMutex);converters.erase(id);}
