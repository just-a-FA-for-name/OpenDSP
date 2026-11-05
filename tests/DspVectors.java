import org.openeq.dsp.Biquad;
public class DspVectors {
 public static void main(String[] args){StringBuilder out=new StringBuilder("[");boolean first=true;for(double rate:new double[]{44100,48000,96000,192000})for(String type:new String[]{"peak","low_shelf","high_shelf","low_pass","high_pass","notch","all_pass"}){
  Biquad b=new Biquad(type,1000,6,.707,rate);if(!first)out.append(',');first=false;out.append("{\"type\":\"").append(type).append("\",\"rate\":").append((int)rate).append(",\"coefficients\":[").append(b.b0).append(',').append(b.b1).append(',').append(b.b2).append(',').append(b.a1).append(',').append(b.a2).append("],\"impulse\":[");for(int n=0;n<256;n++){if(n>0)out.append(',');out.append(b.process(n==0?1:0));}out.append("]}");}System.out.println(out.append(']'));}
}
