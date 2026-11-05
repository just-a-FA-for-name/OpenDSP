package org.openeq.dsp;
/** Digital block levels and signal state; never retains audio samples. */
public final class CaptureMeter {
 public static double rmsDb(double energy,long frames){if(frames<=0||!Double.isFinite(energy)||energy<=0)return Double.NEGATIVE_INFINITY;return 10*Math.log10(energy/(frames*2));}
 public static boolean hasSignal(double rmsDb){return Double.isFinite(rmsDb)&&rmsDb>-90;}
 public static String phase(boolean capture,double rmsDb,long completedFrames){
  if(completedFrames<=0)return "buffering";
  if(!capture)return "processing";
  return hasSignal(rmsDb)?"processing":"waiting";
 }
}
