using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using NAudio.Wave;
namespace OpenEQ;
internal static class DsdReferenceCheck {
 sealed class QuietTone:ISampleProvider {
  long frame;public WaveFormat WaveFormat{get;}=WaveFormat.CreateIeeeFloatWaveFormat(176400,2);
  public int Read(float[] b,int offset,int count){for(int i=0;i<count;i+=2){float x=(float)(.0001*Math.Sin(2*Math.PI*997*frame++/176400));b[offset+i]=b[offset+i+1]=x;}return count;}
 }
 internal static int Run(string path,string python,string script,string id,int seconds){
  path=Path.GetFullPath(path);var report=new Dictionary<string,object>();Process? child=null;
  try{
   if(seconds<1||seconds>120)throw new ArgumentOutOfRangeException(nameof(seconds));
   string raw=path+".dop32";var provider=new DopIntegrityProvider(new DsdProvider(new QuietTone(),Dsd.Dop64,DeviceFormats.Create(176400,32,true)));
   using(var file=File.Create(raw)){var block=new byte[8192*8];long remaining=176400L*seconds*8;while(remaining>0){int n=provider.Read(block,0,(int)Math.Min(block.Length,remaining));file.Write(block,0,n);remaining-=n;}}
   report["frames_verified"]=provider.Verified;report["source_pcm_peak_dbfs"]=20*Math.Log10(.0001);report["tone_peak_dbfs"]=20*Math.Log10(.0001*DsdProvider.InputGain);report["raw"]=raw;
   using var lease=new DopVolumeLease(id);report["original_endpoint_db"]=lease.AttenuationDb;
   var start=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(string arg in new[]{Path.GetFullPath(script),raw,path+".portaudio.json"})start.ArgumentList.Add(arg);
   using var job=new OwnedChildJob();child=Process.Start(start)??throw new IOException("Reference process did not start");job.Assign(child);
   string? ready=child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();if(ready!="READY")throw new IOException("Independent output was not ready: "+ready);
   lease.Activate();report["carrier_db"]=lease.CarrierDb;child.StandardInput.WriteLine("PLAY");child.StandardInput.Flush();
   if(!child.WaitForExit((seconds+30)*1000))throw new TimeoutException("Independent output exceeded its bounded test duration");
   if(child.ExitCode!=0)throw new IOException("Independent output failed: "+child.StandardError.ReadToEnd());
   report["transport_result"]=JsonDocument.Parse(File.ReadAllText(path+".portaudio.json")).RootElement.Clone();report["result"]="TRANSPORT_COMPLETED_AWAIT_LISTENING";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{if(child!=null){if(!child.HasExited){child.Kill(true);child.WaitForExit(3000);}child.Dispose();}report["scope"]="Quiet precomputed DoP via independent PortAudio; no OpenDSP live DSP, clock servo or render backend. Acoustic result requires user feedback.";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
/// <summary>Closing the parent handle terminates its owned reference player
/// before the independent volume guard restores a non-unity endpoint level.</summary>
internal sealed class OwnedChildJob:IDisposable {
 readonly SafeFileHandle handle;
 public OwnedChildJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception();var info=new Extended{Basic=new Basic{LimitFlags=0x2000}};if(!SetInformationJobObject(handle,9,ref info,Marshal.SizeOf<Extended>())){handle.Dispose();throw new System.ComponentModel.Win32Exception();}}
 public void Assign(Process child){if(!AssignProcessToJobObject(handle,child.Handle)){child.Kill(true);throw new System.ComponentModel.Win32Exception();}}
 public void Dispose()=>handle.Dispose();
 [StructLayout(LayoutKind.Sequential)]struct Basic{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
 [StructLayout(LayoutKind.Sequential)]struct Io{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
 [StructLayout(LayoutKind.Sequential)]struct Extended{public Basic Basic;public Io Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateJobObject(IntPtr security,string? name);
 [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref Extended value,int size);
 [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
