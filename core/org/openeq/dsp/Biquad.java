package org.openeq.dsp;

/** RBJ biquads. Q applies to shelves too (not a shelf-slope parameter). */
public final class Biquad {
    public final double b0,b1,b2,a1,a2;
    private double z1,z2;
    public Biquad(String type,double frequency,double gain,double q,double rate) {
        if (!Double.isFinite(rate)||rate<8000 || !Double.isFinite(frequency)||frequency<=0 ||
            frequency>=rate/2 || !Double.isFinite(gain)||Math.abs(gain)>24 || !Double.isFinite(q)||q<0.1||q>30)
            throw new IllegalArgumentException("Invalid filter parameters");
        double w=2*Math.PI*frequency/rate, c=Math.cos(w), s=Math.sin(w), alpha=s/(2*q);
        double A=Math.pow(10,gain/40), t=2*Math.sqrt(A)*alpha;
        double B0,B1,B2,A0,A1,A2;
        switch(type) {
            case "peak": B0=1+alpha*A; B1=-2*c; B2=1-alpha*A; A0=1+alpha/A; A1=-2*c; A2=1-alpha/A; break;
            case "low_shelf": B0=A*((A+1)-(A-1)*c+t);B1=2*A*((A-1)-(A+1)*c);B2=A*((A+1)-(A-1)*c-t);A0=(A+1)+(A-1)*c+t;A1=-2*((A-1)+(A+1)*c);A2=(A+1)+(A-1)*c-t;break;
            case "high_shelf": B0=A*((A+1)+(A-1)*c+t);B1=-2*A*((A-1)+(A+1)*c);B2=A*((A+1)+(A-1)*c-t);A0=(A+1)-(A-1)*c+t;A1=2*((A-1)-(A+1)*c);A2=(A+1)-(A-1)*c-t;break;
            case "low_pass": B0=(1-c)/2;B1=1-c;B2=B0;A0=1+alpha;A1=-2*c;A2=1-alpha;break;
            case "high_pass": B0=(1+c)/2;B1=-(1+c);B2=B0;A0=1+alpha;A1=-2*c;A2=1-alpha;break;
            case "notch": B0=1;B1=-2*c;B2=1;A0=1+alpha;A1=-2*c;A2=1-alpha;break;
            case "all_pass": B0=1-alpha;B1=-2*c;B2=1+alpha;A0=1+alpha;A1=-2*c;A2=1-alpha;break;
            default: throw new IllegalArgumentException("Unsupported filter: "+type);
        }
        b0=B0/A0;b1=B1/A0;b2=B2/A0;a1=A1/A0;a2=A2/A0;
    }
    // Flush inaudibly small states so silence cannot leave costly subnormal tails.
    public double process(double x) {double y=b0*x+z1;z1=b1*x-a1*y+z2;z2=b2*x-a2*y;if(Math.abs(z1)<1e-30)z1=0;if(Math.abs(z2)<1e-30)z2=0;return y;}
    public double magnitude(double f,double rate) {
        double w=2*Math.PI*f/rate,c=Math.cos(w),s=Math.sin(w),c2=Math.cos(2*w),s2=Math.sin(2*w);
        double nr=b0+b1*c+b2*c2,ni=-b1*s-b2*s2,dr=1+a1*c+a2*c2,di=-a1*s-a2*s2;
        return Math.sqrt((nr*nr+ni*ni)/(dr*dr+di*di));
    }
    /** Complex frequency response for offline analysis, never the sample loop. */
    public double[] transfer(double f,double rate) {
        double w=2*Math.PI*f/rate,c=Math.cos(w),s=Math.sin(w),c2=Math.cos(2*w),s2=Math.sin(2*w);
        double nr=b0+b1*c+b2*c2,ni=-b1*s-b2*s2,dr=1+a1*c+a2*c2,di=-a1*s-a2*s2,d=dr*dr+di*di;
        return new double[]{(nr*dr+ni*di)/d,(ni*dr-nr*di)/d};
    }
}
