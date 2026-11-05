using System.Runtime.InteropServices;
namespace OpenEQ;
internal static class ExclusiveOpenRetry {
 internal static void Run(Action open,Action? release=null,Action<int>? wait=null){
  wait??=Thread.Sleep;
  for(int attempt=0;;attempt++)try{open();return;}
  catch(COMException ex)when(ex.HResult==unchecked((int)0x8889000A)&&attempt<4){
   // Stop/Dispose may precede the driver's release by a short interval. Other
   // errors, including unsupported formats, must retain their original meaning.
   release?.Invoke();wait(100*(attempt+1));
  }
 }
}
