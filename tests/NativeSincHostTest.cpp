#include "../android/native/sinc_resampler.h"
#include <fstream>
#include <iostream>
#include <vector>
int main(int argc,char** argv){
 try{
  openeq::SincResampler src(48000,384000,true);for(int i=0;i<1000;i++)src.clockPpm(137);
  std::ofstream output(argv[1],std::ios::binary);std::vector<double> input(2048),out(2048);int at=0;bool eof=false;int64_t count=0;
  while(true){int n=src.read(out.data(),1024);if(n){output.write(reinterpret_cast<const char*>(out.data()),n*2*sizeof(double));count+=n;continue;}
   if(eof)break;if(at==100000){src.finish();eof=true;continue;}
   int frames=std::min(1024,100000-at);for(int i=0;i<frames;i++){double x=.2*std::sin(2*3.14159265358979323846*10003*(at+i)/48000);input[i*2]=x;input[i*2+1]=-x;}src.push(input.data(),frames);at+=frames;
  }
  if(count<790000||count>800001)throw std::runtime_error("SRC duration");
  std::cout<<"PASS: native SRC bounded stream and EOF, frames="<<count<<"\n";return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
