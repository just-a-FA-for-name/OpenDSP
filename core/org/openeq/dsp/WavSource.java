package org.openeq.dsp;
import java.io.*;
public final class WavSource implements PcmSource {
 final InputStream in;int rate,bits,channels,stride,encoding,valid;long remaining;double scale;final byte[] frame=new byte[16];
 public WavSource(InputStream source)throws IOException{
  in=new BufferedInputStream(source,65536);if(!tag().equals("RIFF"))throw new IOException("Only RIFF WAV is supported");u32();if(!tag().equals("WAVE"))throw new IOException("Not WAV");
  boolean format=false;long scanned=0;
  while(true){String id=tag();long size=u32();if(!id.equals("data"))scanned+=size+8;if(scanned>16*1024*1024)throw new IOException("WAV header too large");
   if(id.equals("fmt ")){
    if(size<16||size>65536)throw new IOException("Invalid WAV format");encoding=u16();channels=u16();rate=(int)u32();u32();stride=u16();bits=u16();valid=bits;
    long consumed=16;
    if(encoding==65534){
     if(size<40)throw new IOException("Short extensible format");int extra=u16();valid=u16();u32();byte[] guid=new byte[16];exact(guid,16);consumed=40;
     byte[] suffix={0,0,0,0,16,0,(byte)128,0,0,(byte)170,0,56,(byte)155,113};
     if(extra<22)throw new IOException("Invalid extensible format");
     for(int i=2;i<16;i++)if(guid[i]!=suffix[i-2])throw new IOException("Unsupported WAV subtype");encoding=(guid[0]&255)|((guid[1]&255)<<8);
    }
    if((encoding!=1&&encoding!=3)||(channels!=1&&channels!=2)||rate<8000||rate>768000||stride!=channels*(bits/8)||
       (encoding==1&&(bits!=16&&bits!=24&&bits!=32))||(encoding==3&&(bits!=32&&bits!=64))||valid<1||valid>bits)throw new IOException("Unsupported PCM/float WAV format");
    scale=Math.scalb(1.0,bits-1);skip(size-consumed+(size&1));format=true;
   }else if(id.equals("data")){if(!format||size%stride!=0)throw new IOException("Invalid WAV data");remaining=size;break;}
   else skip(size+(size&1));
  }
 }
 public int rate(){return rate;}public int bits(){return valid;}
 public int read(double[] output,int frames)throws IOException{
  int count=(int)Math.min(frames,remaining/stride);if(frames<0||output.length<frames*2)throw new IOException("Invalid output block");
  for(int i=0;i<count;i++){exact(frame,stride);double l=sample(0),r=channels==2?sample(bits/8):l;output[i*2]=l;output[i*2+1]=r;}remaining-=(long)count*stride;return count;
 }
 double sample(int offset){
  long n=0;for(int i=0;i<bits/8;i++)n|=(long)(frame[offset+i]&255)<<(8*i);
  double value;if(encoding==3)value=bits==32?Float.intBitsToFloat((int)n):Double.longBitsToDouble(n);
  else{long signed=(n<<(64-bits))>>(64-bits);value=signed/scale;}
  return Double.isFinite(value)?value:0;
 }
 int byte1()throws IOException{int v=in.read();if(v<0)throw new EOFException("Truncated WAV");return v;}
 int u16()throws IOException{return byte1()|(byte1()<<8);}long u32()throws IOException{return Integer.toUnsignedLong(byte1()|(byte1()<<8)|(byte1()<<16)|(byte1()<<24));}
 String tag()throws IOException{return ""+(char)byte1()+(char)byte1()+(char)byte1()+(char)byte1();}
 void exact(byte[] bytes,int size)throws IOException{for(int at=0;at<size;){int n=in.read(bytes,at,size-at);if(n<0)throw new EOFException("Truncated WAV");if(n==0)continue;at+=n;}}
 void skip(long size)throws IOException{if(size<0)throw new IOException("Invalid chunk size");while(size>0){long n=in.skip(size);if(n==0){byte1();n=1;}size-=n;}}
 public void close()throws IOException{in.close();}
}
