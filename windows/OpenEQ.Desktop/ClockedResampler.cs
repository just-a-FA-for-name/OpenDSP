
using NAudio.Wave;

namespace OpenEQ;
/// <summary>Output-driven sinc ASRC with a slow buffer servo for independent USB and virtual-device clocks.</summary>
public sealed class ClockedResampler:ISampleProvider {
 readonly ISampleProvider source;readonly Func<double> bufferSeconds;readonly RealtimeWdlResampler converter=new();
 readonly double inputRate;double filteredError,correction;readonly int channels;
 public WaveFormat WaveFormat{get;}public double CorrectionPpm=>correction*1e6; public int FilterBuilds=>converter.FilterBuilds;
 public double TargetBufferSeconds{get;set;}=.1;
 public ClockedResampler(ISampleProvider source,int outputRate,Func<double> bufferSeconds,int taps=256,int phases=256){this.source=source;this.bufferSeconds=bufferSeconds;inputRate=source.WaveFormat.SampleRate;channels=source.WaveFormat.Channels;WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(outputRate,channels);converter.SetMode(true,2,true,taps,phases);converter.SetFilterParms();converter.SetFeedMode(false);converter.SetRates(inputRate,outputRate);converter.PrepareAdaptive(inputRate/outputRate*1.002,checked(((int)inputRate+taps*4+16)*channels));}
 public int Read(float[] buffer,int offset,int count){
  // Average packet-level jitter; positive fill error consumes input slightly faster.
  filteredError+=(bufferSeconds()-TargetBufferSeconds-filteredError)*.01;double target=Math.Clamp(filteredError*.01,-.002,.002);correction+=(target-correction)*.02;
  converter.SetRates(inputRate*(1+correction),WaveFormat.SampleRate);
  int requested=count/channels;int needed=converter.ResamplePrepare(requested,channels,out var input,out int start);int available=source.Read(input,start,needed*channels)/channels;
  return converter.ResampleOut(buffer,offset,available,requested,channels)*channels;
 }
}
