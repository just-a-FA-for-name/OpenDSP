import org.openeq.dsp.*;
import java.nio.*;
import java.nio.file.*;
public final class NativeSincParityTest {
 public static void main(String[] args)throws Exception{
  ByteBuffer expected=ByteBuffer.wrap(Files.readAllBytes(Paths.get(args[0]))).order(ByteOrder.LITTLE_ENDIAN);
  SincSource src=new SincSource(new AudioIoTest.Tone(48000,100000,10003),384000,true);for(int i=0;i<1000;i++)src.clockPpm(137);
  double[] out=new double[2048];double error=0;int n;
  while((n=src.read(out,1024))>0)for(int i=0;i<n*2;i++){if(expected.remaining()<8)throw new AssertionError("Native duration short");error=Math.max(error,Math.abs(out[i]-expected.getDouble()));}
  if(expected.hasRemaining()||error>1e-9)throw new AssertionError("Native/Java SRC mismatch "+error);
  System.out.println("PASS: native/Java SRC sample parity, error="+error);
 }
}
