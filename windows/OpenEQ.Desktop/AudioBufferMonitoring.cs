using NAudio.Wave;
namespace OpenEQ;
/// <summary>Pads only the bytes actually missing; counts partial as well as empty reads.</summary>
internal sealed class PaddedCaptureProvider:IWaveProvider {
 readonly BufferedWaveProvider source;readonly Action<int> missing;
 public PaddedCaptureProvider(BufferedWaveProvider source,Action<int> missing){this.source=source;this.missing=missing;source.ReadFully=false;}
 public WaveFormat WaveFormat=>source.WaveFormat;
 public int Read(byte[] buffer,int offset,int count){int read=source.Read(buffer,offset,count);if(read<count){Array.Clear(buffer,offset+read,count-read);missing(count-read);}return count;}
}
/// <summary>Observes generation cost without changing payload or buffering policy.</summary>
internal sealed class TimedRenderProvider(IWaveProvider source):IWaveProvider {
 double maxMs;long slowReads;
 public double MaxReadMs=>Volatile.Read(ref maxMs);public long SlowReads=>Interlocked.Read(ref slowReads);
 public WaveFormat WaveFormat=>source.WaveFormat;
 public int Read(byte[] buffer,int offset,int count){long start=System.Diagnostics.Stopwatch.GetTimestamp();int read=source.Read(buffer,offset,count);
  double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;if(elapsed>maxMs)Volatile.Write(ref maxMs,elapsed);
  // A slow generation call is a diagnostic, not proof of a hardware underrun:
  // the render backend may already have queued additional audio.
  if(read>0&&elapsed>1000d*read/WaveFormat.AverageBytesPerSecond)Interlocked.Increment(ref slowReads);return read;
 }
}
