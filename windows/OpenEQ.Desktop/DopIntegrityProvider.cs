using NAudio.Wave;
namespace OpenEQ;
/// <summary>Final software boundary: one wrong marker is enough to unlock a
/// DoP receiver. Reject malformed frames instead of sending them as noise.</summary>
internal sealed class DopIntegrityProvider:IWaveProvider {
 readonly IWaveProvider source;long verified,invalid;
 public WaveFormat WaveFormat=>source.WaveFormat;
 public long Verified=>Interlocked.Read(ref verified);public long Invalid=>Interlocked.Read(ref invalid);
 public DopIntegrityProvider(IWaveProvider source){this.source=source;if(WaveFormat.Channels!=2||WaveFormat.BitsPerSample is not (24 or 32))throw new ArgumentException("DoP integrity requires stereo 24/32-bit");}
 public int Read(byte[] bytes,int offset,int count){int n=source.Read(bytes,offset,count),stride=WaveFormat.BlockAlign,width=WaveFormat.BitsPerSample/8;
  if(n<0||n>count||n%stride!=0)throw new IOException("Invalid final DoP byte count");
  for(int i=0;i<n;i+=stride){byte marker=(byte)((verified&1)==0?0x05:0xFA);int pos=offset+i;
   bool valid=bytes[pos+width-1]==marker&&bytes[pos+2*width-1]==marker&&(width!=4||(bytes[pos]==0&&bytes[pos+width]==0));
   if(!valid){Interlocked.Increment(ref invalid);throw new IOException($"DoP integrity failed at frame {verified}; output rejected");}Interlocked.Increment(ref verified);
  }return n;
 }
}
