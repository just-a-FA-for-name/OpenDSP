extern "C" JNIEXPORT jlong JNICALL Java_org_openeq_app_UsbNative_open(JNIEnv* env,jclass,jint fd,jint rate,jint bits,jboolean dop){
 try{auto stream=std::make_shared<Stream>(fd,rate,bits,dop);jlong id;{std::lock_guard<std::mutex> lock(sessionsMutex);id=nextSession++;sessions[id]=stream;}try{stream->launch();}catch(...){std::lock_guard<std::mutex> lock(sessionsMutex);sessions.erase(id);throw;}return id;}
 catch(const std::exception& ex){error(env,ex);return 0;}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_writePcm(JNIEnv* env,jclass,jlong id,jdoubleArray samples,jint count){
 try{auto s=session(id);require(!s->dopMode&&count>0&&count<=8192&&count%2==0&&count<=env->GetArrayLength(samples),"Invalid PCM block");
  std::lock_guard<std::mutex> lock(s->producer);env->GetDoubleArrayRegion(samples,0,count,s->pcmScratch.data());if(env->ExceptionCheck())return;
  uint64_t clipped=0,invalid=0;
  for(int i=0;i<count;i++){double value=s->pcmScratch[i];if(!std::isfinite(value))invalid++;else if(value>1||value< -1)clipped++;usb_audio::pcm(value,s->format.bytes,s->format.bits,s->byteScratch.data()+i*s->format.bytes);}
  s->pcmClippedSamples+=clipped;s->pcmInvalidSamples+=invalid;
  s->push(s->byteScratch.data(),size_t(count)*s->format.bytes);
 }catch(const std::exception& ex){error(env,ex);}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_writeDop(JNIEnv* env,jclass,jlong id,jbyteArray payload,jint count){
 try{auto s=session(id);require(s->dopMode&&count>0&&count<=32768&&count%4==0&&count<=env->GetArrayLength(payload),"Invalid DoP payload");
  std::lock_guard<std::mutex> lock(s->producer);env->GetByteArrayRegion(payload,0,count,reinterpret_cast<jbyte*>(s->byteScratch.data()));if(env->ExceptionCheck())return;s->push(s->byteScratch.data(),count);
 }catch(const std::exception& ex){error(env,ex);}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_finish(JNIEnv* env,jclass,jlong id){
 try{auto s=session(id);s->eof=true;s->changed.notify_all();std::unique_lock<std::mutex> lock(s->mutex);require(s->changed.wait_for(lock,std::chrono::seconds(5),[&]{return s->finished.load();}),"USB drain timed out");require(s->failure.empty(),s->failure);}
 catch(const std::exception& ex){error(env,ex);}
}
extern "C" JNIEXPORT jstring JNICALL Java_org_openeq_app_UsbNative_status(JNIEnv* env,jclass,jlong id){try{return env->NewStringUTF(session(id)->status().c_str());}catch(const std::exception& ex){error(env,ex);return nullptr;}}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_cancel(JNIEnv*,jclass,jlong id){try{session(id)->requestStop();}catch(...){}}
extern "C" JNIEXPORT jstring JNICALL Java_org_openeq_app_UsbNative_awaitStopped(JNIEnv* env,jclass,jlong id,jint timeoutMs){
 try{auto s=session(id);require(timeoutMs>0&&timeoutMs<=5000,"Invalid stop timeout");std::unique_lock<std::mutex> lock(s->mutex);
  require(s->changed.wait_for(lock,std::chrono::milliseconds(timeoutMs),[&]{return s->finished.load();}),"USB cancellation has not completed");
  lock.unlock();return env->NewStringUTF(s->status().c_str());
 }catch(const std::exception& ex){error(env,ex);return nullptr;}
}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_attach(JNIEnv*,jclass,jint fd,jint interfaceId){try{Connection connection(fd);libusb_attach_kernel_driver(connection.handle,interfaceId);}catch(...){}}
extern "C" JNIEXPORT void JNICALL Java_org_openeq_app_UsbNative_close(JNIEnv* env,jclass,jlong id){
 try{auto s=session(id);s->requestStop();if(s->worker.joinable())s->worker.join();std::lock_guard<std::mutex> lock(sessionsMutex);sessions.erase(id);}
 catch(const std::exception& ex){error(env,ex);}
}
