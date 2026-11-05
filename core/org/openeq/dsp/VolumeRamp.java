package org.openeq.dsp;
/** Independent post-DSP attenuation, bounded to unity, with a 20ms gain ramp. */
public final class VolumeRamp {
 final int length;int remaining;double gain,target,step;
 public VolumeRamp(int rate,double initial){if(rate<8000||rate>768000)throw new IllegalArgumentException("rate");length=Math.max(1,rate/50);gain=target=checked(initial);}
 static double checked(double value){if(!Double.isFinite(value)||value<0||value>1)throw new IllegalArgumentException("volume gain");return value;}
 public void target(double value){value=checked(value);if(value==target)return;target=value;remaining=length;step=(target-gain)/length;}
 public double next(){if(remaining>0){gain+=step;if(--remaining==0)gain=target;}return Math.max(0,Math.min(1,gain));}
 public double current(){return gain;}
}
