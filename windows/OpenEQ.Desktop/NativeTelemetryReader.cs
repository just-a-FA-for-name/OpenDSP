using System.IO.MemoryMappedFiles;
namespace OpenEQ;
internal sealed class NativeTelemetryReader:IDisposable {
 readonly FileStream stream;readonly MemoryMappedFile map;readonly MemoryMappedViewAccessor view;
 public NativeTelemetryReader(string path){stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);try{map=MemoryMappedFile.CreateFromFile(stream,null,4096,MemoryMappedFileAccess.Read,HandleInheritability.None,true);try{view=map.CreateViewAccessor(0,4096,MemoryMappedFileAccess.Read);}catch{map.Dispose();throw;}}catch{stream.Dispose();throw;}}
 public double InputEnergy=>view.ReadDouble(1456)+view.ReadDouble(1464);
 public double OutputEnergy=>view.ReadDouble(1472)+view.ReadDouble(1480);
 public long Frames=>view.ReadInt64(1448);
 public uint Rate=>view.ReadUInt32(1488);
 public long Clipped=>view.ReadInt64(1504);
 public void Dispose(){view.Dispose();map.Dispose();stream.Dispose();}
}
