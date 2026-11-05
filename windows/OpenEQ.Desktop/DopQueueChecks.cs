using System.Text.Json;
using NAudio.Wave;
namespace OpenEQ;
internal static class DopQueueChecks {
 sealed class Pattern(int bits,bool fail=false):IWaveProvider {
  long position;public WaveFormat WaveFormat{get;}=new WaveFormat(176400,bits,2);
  public int Read(byte[] b,int offset,int count){if(fail)throw new IOException("injected synthesis failure");for(int i=0;i<count;i++)b[offset+i]=(byte)(position++%251);return count;}
 }
 public static int Run(string path){var report=new Dictionary<string,object>();var checks=new List<string>();
  void Check(bool yes,string name){if(!yes)throw new Exception(name);checks.Add(name);}
  try{
   foreach(int bits in new[]{24,32}){
    using var queue=new DopSynthesisQueue(new Pattern(bits),()=>1,160);queue.Start();Check(queue.QueuedMilliseconds>=350,$"{bits}_prebuffer_ready");
    var bytes=new byte[8192];long position=0;for(int i=0;i<2000;i++){
     int count=(1+(i*139)%997)*(bits/8*2);CheckRead(queue.Read(bytes,7,count),count);
     for(int j=0;j<count;j++)if(bytes[7+j]!=(byte)(position++%251))throw new Exception("Queue reordered or changed payload");
    }
    Check(true,$"{bits}_odd_reads_wraparound_byte_exact");report[$"{bits}_bytes_checked"]=position;
   }
   bool fault=false;using(var queue=new DopSynthesisQueue(new Pattern(24,true),()=>1,160)){try{queue.Start();}catch(IOException e){fault=e.InnerException?.Message=="injected synthesis failure";}}
   Check(fault,"worker_fault_propagates_without_zero_padding");report["result"]="PASS";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["checks"]=checks;report["scope"]="Offline synthesis worker prebuffer, concurrent wraparound, exact byte ordering, fault propagation and shutdown";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
 static void CheckRead(int n,int expected){if(n!=expected)throw new Exception("Queue short read");}
}
