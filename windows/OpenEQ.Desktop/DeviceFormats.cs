using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace OpenEQ;
public sealed record OutputFormat(int Rate,int Bits,bool Extensible);
public sealed class DeviceFormats {
 public string DeviceId{get;init;}="";public int MixRate{get;init;}public int MixBits{get;init;}public long QueryMilliseconds{get;set;}
 public List<OutputFormat> Exclusive{get;init;}=new();
 // WASAPI exposes a format query, not an exhaustive enumeration. Include common
 // PCM clock families and the actual driver mix rate; a custom rate is also queried.
 public static DeviceFormats Read(string id,int customRate=0){var watch=Stopwatch.StartNew();using var en=new MMDeviceEnumerator();using var device=en.GetDevice(id);var client=device.AudioClient;var mix=client.MixFormat;var result=new DeviceFormats{DeviceId=id,MixRate=mix.SampleRate,MixBits=mix.BitsPerSample};
  var rates=new[]{8000,11025,12000,16000,22050,24000,32000,44100,48000,64000,88200,96000,128000,176400,192000,256000,352800,384000,512000,705600,768000,1024000,1411200,1536000,mix.SampleRate,customRate}.Where(r=>r>=8000&&r<=1536000).Distinct().Order();
  foreach(var rate in rates)foreach(var bits in new[]{16,24,32})foreach(bool extensible in new[]{false,true}){var format=Create(rate,bits,extensible);bool supported;try{supported=client.IsFormatSupported(AudioClientShareMode.Exclusive,format);}catch{supported=false;}if(supported){result.Exclusive.Add(new OutputFormat(rate,bits,extensible));break;}}
  result.QueryMilliseconds=watch.ElapsedMilliseconds;return result;
 }
 public static WaveFormat Create(int rate,int bits,bool extensible=false){if(!extensible)return new WaveFormat(rate,bits,2);
  // NAudio's 32-bit extensible constructor chooses float. Serialize PCM explicitly.
  using var data=new MemoryStream();using(var writer=new BinaryWriter(data,System.Text.Encoding.UTF8,true)){writer.Write((ushort)0xfffe);writer.Write((ushort)2);writer.Write(rate);writer.Write(rate*2*bits/8);writer.Write((ushort)(2*bits/8));writer.Write((ushort)bits);writer.Write((ushort)22);writer.Write((ushort)bits);writer.Write(3);writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());}var memory=Marshal.AllocHGlobal(40);try{Marshal.Copy(data.ToArray(),0,memory,40);return WaveFormat.MarshalFromPtr(memory);}finally{Marshal.FreeHGlobal(memory);}
 }
 public static WaveFormat Require(string id,int rate,int bits){using var en=new MMDeviceEnumerator();using var d=en.GetDevice(id);foreach(bool ext in new[]{false,true}){var f=Create(rate,bits,ext);if(d.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive,f))return f;}throw new ArgumentException($"设备驱动不支持 {rate:N0} Hz / {bits}-bit 双声道 PCM 独占。请刷新设备格式或选择已验证的组合。");}
 public static int Report(string path){var data=AudioEngine.Devices().Where(d=>d.Transport!="虚拟").Select(d=>new{device=d,formats=Read(d.Id)}).ToArray();File.WriteAllText(path,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}
}
