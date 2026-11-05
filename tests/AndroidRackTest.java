package org.openeq.app;
import java.io.*;
import java.nio.*;
import java.nio.file.*;
import org.json.*;
public final class AndroidRackTest {
 public static void main(String[] args)throws Exception{
  Preset p=Preset.parse(new String(Files.readAllBytes(Paths.get(args[0])),"UTF-8"));Preset again=Preset.parse(p.encode());
  if(!again.processing.getJSONObject("spatial").getBoolean("enabled"))throw new AssertionError("Spatial configuration lost");
  DspRack rack=new DspRack(384000,p);ByteBuffer expected=ByteBuffer.wrap(Files.readAllBytes(Paths.get(args[1]))).order(ByteOrder.LITTLE_ENDIAN);double error=0;
  for(int i=0;expected.remaining()>=16;i++){rack.process(.1*Math.sin(i*.03),.07*Math.cos(i*.017));error=Math.max(error,Math.max(Math.abs(rack.left-expected.getDouble()),Math.abs(rack.right-expected.getDouble())));}
  if(error>1e-9)throw new AssertionError("Windows/Android DSP mismatch "+error);
  rack.process(Double.NaN,Double.POSITIVE_INFINITY);for(int i=0;i<10000;i++)rack.process(.1,.1);if(!Double.isFinite(rack.left)||!Double.isFinite(rack.right))throw new AssertionError("Invalid state survived");
  Preset next=p.copy();next.preamp=-6;rack.configure(next);for(int i=0;i<20000;i++)rack.process(.1,-.1);
  System.out.println("PASS: full desktop/Android DSP parity at384k, error="+error+"; serialization, updates, NaN protection");
 }
}
