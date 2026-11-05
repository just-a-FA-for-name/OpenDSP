package org.openeq.dsp;

/** Stereo output stage with linear balance, integer-sample delays and linked sample-peak protection. */
public final class StereoStage {
    private final double[] leftDelay,rightDelay;
    private final double rate,fade,release;
    private int index,dl,dr;
    private double leftGain=1,rightGain=1,targetGain=1,currentGain=0,ceiling=1,protectionGain=1;
    private boolean protection=true;
    private boolean compressor;private double threshold,ratio,knee,makeup,attack,compRelease,compGain=1;
    public double left,right;
    public long clippedFrames;
    public StereoStage(double sampleRate){this(sampleRate,100);}
    public StereoStage(double sampleRate,double maximumDelayMs){rate=sampleRate;leftDelay=new double[Math.max(1,(int)Math.round(rate*maximumDelayMs/1000)+1)];rightDelay=new double[leftDelay.length];fade=1-Math.exp(-1/(rate*.005));release=1-Math.exp(-1/(rate*.1));}
    public void compressor(boolean enabled,double threshold,double ratio,double knee,double makeup,double attackMs,double releaseMs){
        this.compressor=enabled;this.threshold=threshold;this.ratio=ratio;this.knee=knee;this.makeup=makeup;
        attack=1-Math.exp(-1/(rate*Math.max(.1,attackMs)/1000));compRelease=1-Math.exp(-1/(rate*Math.max(5,releaseMs)/1000));
    }
    public void configure(double gainDb,double balance,double leftMs,double rightMs,boolean invertLeft,boolean invertRight,boolean protect,double ceilingDb){
        targetGain=Math.pow(10,gainDb/20);leftGain=(invertLeft?-1:1)*Math.min(1,1-balance);rightGain=(invertRight?-1:1)*Math.min(1,1+balance);
        dl=(int)Math.round(leftMs*rate/1000);dr=(int)Math.round(rightMs*rate/1000);if(dl<0||dr<0||dl>=leftDelay.length||dr>=rightDelay.length)throw new IllegalArgumentException("Delay exceeds 100 ms");
        protection=protect;ceiling=Math.pow(10,ceilingDb/20);
    }
    public void process(double l,double r){
        if(!Double.isFinite(l))l=0;if(!Double.isFinite(r))r=0;
        currentGain+=(targetGain-currentGain)*fade;
        leftDelay[index]=l*currentGain*leftGain;rightDelay[index]=r*currentGain*rightGain;
        left=leftDelay[(index-dl+leftDelay.length)%leftDelay.length];right=rightDelay[(index-dr+rightDelay.length)%rightDelay.length];index=(index+1)%leftDelay.length;
        if(compressor){double over=20*Math.log10(Math.max(1e-9,Math.max(Math.abs(left),Math.abs(right))))-threshold;
            double gr=over>knee/2?over*(1/ratio-1):over>-knee/2&&knee>0?(1/ratio-1)*(over+knee/2)*(over+knee/2)/(2*knee):0;
            double target=Math.pow(10,(gr+makeup)/20);compGain+=(target-compGain)*(target<compGain?attack:compRelease);left*=compGain;right*=compGain;}
        if(protection){double peak=Math.max(Math.abs(left),Math.abs(right));double desired=peak>ceiling?ceiling/peak:1;
            protectionGain=desired<protectionGain?desired:Math.min(desired,protectionGain+(1-protectionGain)*release);left*=protectionGain;right*=protectionGain;}
        if(Math.abs(left)>1||Math.abs(right)>1)clippedFrames++;
        left=Math.max(-1,Math.min(1,left));right=Math.max(-1,Math.min(1,right));
    }
}
