using System.Buffers.Binary;
using System.Text.Json;
namespace OpenEQ;

// Physical PCM format and the audio engine float mix format have separate lifetimes.
internal sealed record NativeFormatState(byte[] Endpoint, byte[] Mix) {
 public sealed record Info(int Rate,int Bits,int Channels,int ValidBits,Guid Encoding,int Mask);
 public static Info Describe(byte[] bytes) {
  if(bytes.Length<18||bytes.Length!=18+BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(16)))throw new InvalidDataException("音频格式数据长度无效");
  int tag=BinaryPrimitives.ReadUInt16LittleEndian(bytes),channels=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)),rate=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)),bits=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));
  int valid=bits,mask=channels==2?3:channels==1?4:0;
  Guid encoding=tag==1?new("00000001-0000-0010-8000-00aa00389b71"):tag==3?new("00000003-0000-0010-8000-00aa00389b71"):Guid.Empty;
  if(tag==0xfffe){if(bytes.Length<40)throw new InvalidDataException("扩展音频格式数据不完整");valid=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(18));mask=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20));encoding=new Guid(bytes.AsSpan(24,16));}
  int block=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12));long average=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
  if(rate<8000||rate>1536000||channels<1||channels>32||bits is not (8 or 16 or 24 or 32 or 64)||valid<1||valid>bits||encoding==Guid.Empty||block!=channels*bits/8||average!=(long)rate*block)throw new InvalidDataException("音频格式字段无效");
  return new(rate,bits,channels,valid,encoding,mask);
 }
 public void Validate(){Describe(Endpoint);Describe(Mix);}
 public bool SameAs(NativeFormatState other)=>Describe(Endpoint)==Describe(other.Endpoint)&&Describe(Mix)==Describe(other.Mix);
}
internal interface INativeFormatBackend {NativeFormatState Read();void Write(NativeFormatState state);}
internal sealed record NativeFormatBackup(string DeviceId,DateTimeOffset SavedAt,NativeFormatState Original,NativeFormatState Expected);
internal sealed class NativeFormatTransaction(INativeFormatBackend backend,string path,string deviceId) {
 NativeFormatBackup Load(){var b=JsonSerializer.Deserialize<NativeFormatBackup>(File.ReadAllText(path))??throw new InvalidDataException("格式备份无效");if(b.DeviceId!=deviceId)throw new InvalidDataException("格式备份不属于此设备");b.Original.Validate();b.Expected.Validate();return b;}
 void Save(NativeFormatBackup backup){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,backup);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 static void RequireOwned(NativeFormatState current,NativeFormatBackup backup){if(!current.SameAs(backup.Expected)&&!current.SameAs(backup.Original))throw new InvalidOperationException("设备格式已被其他程序或系统设置改变，已保留当前设置；请先检查原始格式备份");}
 public NativeFormatState Apply(NativeFormatState desired){desired.Validate();var before=backend.Read();before.Validate();NativeFormatBackup? previous=File.Exists(path)?Load():null;if(previous!=null)RequireOwned(before,previous);
  // Persist recovery data before touching the system, including interruption recovery.
  var pending=new NativeFormatBackup(deviceId,previous?.SavedAt??DateTimeOffset.UtcNow,previous?.Original??before,desired);Save(pending);
  try{backend.Write(desired);var actual=backend.Read();if(!actual.SameAs(desired))throw new InvalidOperationException("Windows 实际设备位深、采样率或混音格式与请求不一致");Save(pending with{Expected=actual});return actual;}
  catch(Exception changeError){try{backend.Write(before);if(!backend.Read().SameAs(before))throw new IOException("回退后的实际设备格式与修改前不一致");}catch(Exception rollbackError){throw new AggregateException("格式切换失败且回退未确认；恢复备份已保留，请停止处理",changeError,rollbackError);}if(previous==null)File.Delete(path);else Save(previous);throw;}
 }
 public NativeFormatState Restore(){if(!File.Exists(path))throw new InvalidOperationException("此设备没有 OpenEQ 保存的原始格式；不会用驱动默认值冒充原始设置");var backup=Load();var before=backend.Read();RequireOwned(before,backup);NativeFormatState actual;
  try{backend.Write(backup.Original);actual=backend.Read();if(!actual.SameAs(backup.Original))throw new IOException("原始格式恢复未通过回读确认，已保留备份");}
  catch(Exception restoreError){try{backend.Write(before);if(!backend.Read().SameAs(before))throw new IOException("恢复失败后的回退未通过回读确认");}catch(Exception rollbackError){throw new AggregateException("原始格式恢复与回退均未确认；备份已保留，请停止处理",restoreError,rollbackError);}throw;}
  File.Move(path,path+".restored-"+DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N"));return actual;
 }
}
