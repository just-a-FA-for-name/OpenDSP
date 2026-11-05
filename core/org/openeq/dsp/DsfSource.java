package org.openeq.dsp;
import java.io.*;
/** Stereo DSF DSD64/128 -> 16-bit-per-channel DoP payload; DSP is intentionally bypassed. */
public final class DsfSource implements Closeable {
 final InputStream in;public final int dsdRate,carrierRate;final int order,block;final byte[] left,right;long bytesLeft;int offset;boolean loaded;
 public DsfSource(InputStream source)throws IOException{
  in=new BufferedInputStream(source,65536);if(!tag().equals("DSD ")||u64()!=28)throw new IOException("Not a DSF file");u64();u64();
  if(!tag().equals("fmt ")||u64()!=52)throw new IOException("Unsupported DSF header");
  if(u32()!=1||u32()!=0)throw new IOException("Unsupported DSF version/format");long channelType=u32(),channels=u32();dsdRate=(int)u32();order=(int)u32();long samples=u64();block=(int)u32();u32();
  if(channelType!=2||channels!=2||(dsdRate!=2822400&&dsdRate!=5644800)||(order!=1&&order!=8)||block<16||block>65536||block%2!=0||samples<16||samples%16!=0)throw new IOException("Requires stereo DSD64/128 DSF with complete DoP frames");
  if(!tag().equals("data"))throw new IOException("Missing DSF data");long data=u64()-12;bytesLeft=samples/8;
  long blocks=(bytesLeft+block-1)/block;if(blocks>Long.MAX_VALUE/(block*2L)||data<blocks*block*2L)throw new IOException("Truncated DSF declaration");
  left=new byte[block];right=new byte[block];carrierRate=dsdRate/16;
 }
 public int read(byte[] payload,int maxFrames)throws IOException{
  if(maxFrames<0||payload.length<maxFrames*4)throw new IOException("Invalid DoP buffer");int count=0;
  while(count<maxFrames&&bytesLeft>=2){
   if(!loaded||offset==block){exact(left);exact(right);offset=0;loaded=true;}
   if(offset+1>=block)throw new IOException("DSF block must align DoP words");
   // Earlier DSD byte occupies the high byte of the 16-bit MSB-first word.
   payload[count*4]=convert(left[offset+1]);payload[count*4+1]=convert(left[offset]);
   payload[count*4+2]=convert(right[offset+1]);payload[count*4+3]=convert(right[offset]);
   offset+=2;bytesLeft-=2;count++;
  }return count;
 }
 byte convert(byte value){return order==8?value:(byte)(Integer.reverse(value&255)>>>24);}
 int read1()throws IOException{int n=in.read();if(n<0)throw new EOFException("Truncated DSF");return n;}
 String tag()throws IOException{return ""+(char)read1()+(char)read1()+(char)read1()+(char)read1();}
 long u32()throws IOException{return Integer.toUnsignedLong(read1()|(read1()<<8)|(read1()<<16)|(read1()<<24));}
 long u64()throws IOException{long lo=u32(),hi=u32();if((hi&0x80000000L)!=0)throw new IOException("DSF length overflow");return lo|(hi<<32);}
 void exact(byte[] b)throws IOException{for(int i=0;i<b.length;){int n=in.read(b,i,b.length-i);if(n<0)throw new EOFException("Truncated DSF");if(n>0)i+=n;}}
 public void close()throws IOException{in.close();}
}
