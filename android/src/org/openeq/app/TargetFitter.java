package org.openeq.app;
import java.util.*;
import org.json.*;
import org.openeq.dsp.Biquad;

/** Desktop target-fitting algorithm at the selected DSP rate; never plays audio. */
final class TargetFitter {
 static final class Curve {
  String id,name,kind,rig,source,description;double[][] points;
  Curve(JSONObject j)throws Exception{id=j.getString("id");name=j.getString("name");kind=j.getString("kind");rig=j.getString("rig");source=j.getString("source");description=j.getString("description");points=points(j.getJSONArray("points"));}
  boolean measured(){return kind.equals("acoustic");}
 }
 static final class Options {int bands=20;double min=20,max=8000,boost=6,cut=12,q=3;boolean smooth=true;}
 static final class Result {Preset preset;double before,after,maximum;int clipped;double[][] response;String base;int rate;}
 static double[][] points(JSONArray array)throws Exception{
  double[][] values=new double[array.length()][2];for(int i=0;i<values.length;i++){JSONArray row=array.getJSONArray(i);if(row.length()!=2)throw new Exception("每行必须是频率、电平");values[i][0]=row.getDouble(0);values[i][1]=row.getDouble(1);}Arrays.sort(values,Comparator.comparingDouble(p->p[0]));validate(values);return values;
 }
 static double[][] csv(String text)throws Exception{
  if(text.length()>524288)throw new Exception("频响文件过大");ArrayList<double[]> rows=new ArrayList<>();
  for(String raw:text.split("\\n")){String line=raw.trim().replace("\ufeff","");if(line.isEmpty()||line.startsWith("#")||line.startsWith(";"))continue;String[] parts=line.split("[,;\\t ]+");if(rows.isEmpty()&&parts.length>=2&&Arrays.asList("frequency","freq","hz").contains(parts[0].toLowerCase(Locale.ROOT)))continue;if(parts.length<2)throw new Exception("每行应为 frequency,db");rows.add(new double[]{Double.parseDouble(parts[0]),Double.parseDouble(parts[1])});}
  double[][] result=rows.toArray(new double[0][]);Arrays.sort(result,Comparator.comparingDouble(p->p[0]));validate(result);return result;
 }
 static void validate(double[][] p)throws Exception{
  if(p.length<3||p.length>8192)throw new Exception("频响需要3至8192点");double last=0;for(double[] row:p){Preset.bounded(row[0],10,40000,"频率");Preset.bounded(row[1],-160,160,"电平");if(row[0]<=last)throw new Exception("频率须递增且不重复");last=row[0];}
 }
 static double at(double[][] p,double hz){if(hz<=p[0][0])return p[0][1];if(hz>=p[p.length-1][0])return p[p.length-1][1];int low=0,high=p.length-1;while(high-low>1){int mid=(low+high)/2;if(p[mid][0]>hz)high=mid;else low=mid;}return p[low][1]+(p[high][1]-p[low][1])*Math.log(hz/p[low][0])/Math.log(p[high][0]/p[low][0]);}
 static double shape(double[][] p,double hz,Options o,boolean acoustic){if(!o.smooth||!acoustic)return at(p,hz);double sum=0;for(int j=-4;j<=4;j++)sum+=at(p,clamp(hz*Math.pow(2,j/48d),o.min,o.max));return sum/9;}
 static double clamp(double v,double low,double high){return Math.max(low,Math.min(high,v));}
 static double round(double v,int n){double factor=Math.pow(10,n);return Math.rint(v*factor)/factor;}
 static double score(double[] v){double sum=0;for(double x:v)sum+=x*x;return sum;}
 static double[] response(Preset.Band b,int rate,double[] hz){Biquad filter=b.filter(rate);double[] r=new double[hz.length];for(int i=0;i<r.length;i++)r[i]=20*Math.log10(Math.max(1e-12,filter.magnitude(hz[i],rate)));return r;}
 static boolean bounded(double[] desired,double[] remaining,Options o){for(int i=0;i<desired.length;i++){double eq=desired[i]-remaining[i];if(eq>o.boost+.25||eq< -o.cut-.25)return false;}return true;}
 static Preset.Band band(String type,double hz,double gain,double q){Preset.Band b=new Preset.Band();b.type=type;b.hz=round(hz,1);b.gain=round(gain,2);b.q=type.equals("peak")?q:.707;return b;}
 static Result fit(Curve target,double[][] measured,String rig,int rate,Options o,Preset current)throws Exception{
  Preset.bounded(rate,8000,768000,"采样率");Preset.bounded(o.bands,1,32,"段数");Preset.bounded(o.min,20,19999,"下限");Preset.bounded(o.max,o.min+1,Math.min(20000,rate*.49),"上限");
  Preset.bounded(o.boost,0,12,"最大提升");Preset.bounded(o.cut,.1,24,"最大衰减");Preset.bounded(o.q,.3,8,"最大Q");
  if(target.measured()&&(measured==null||!target.rig.equalsIgnoreCase(rig)))throw new Exception("声学目标需要同测量体系的原始频响："+target.rig);
  if(!target.measured()&&measured!=null)throw new Exception("听感偏好是相对EQ，不减去声学测量");
  if(measured!=null){validate(measured);coverage(measured,o);}
  if(target.measured())coverage(target.points,o);
  int count=160;double[] hz=new double[count],desired=new double[count],raw=new double[count],ms=new double[count],ts=new double[count];
  double ta=target.measured()?at(target.points,1000):0,ma=measured==null?0:at(measured,1000);Result result=new Result();result.base=PresetSync.hash(current);result.rate=rate;
  for(int i=0;i<count;i++){hz[i]=o.min*Math.pow(o.max/o.min,i/(count-1d));ts[i]=shape(target.points,hz[i],o,target.measured())-ta;ms[i]=measured==null?0:shape(measured,hz[i],o,true)-ma;raw[i]=ts[i]-ms[i];desired[i]=clamp(raw[i],-o.cut,o.boost);if(Math.abs(raw[i]-desired[i])>1e-9)result.clipped++;}
  double[] residual=desired.clone();ArrayList<Preset.Band> bands=new ArrayList<>();ArrayList<double[]> contributions=new ArrayList<>();
  for(int iteration=0;iteration<o.bands;iteration++){
   if(Thread.currentThread().isInterrupted())throw new Exception("拟合已取消");if(Math.sqrt(score(residual)/count)<.08)break;
   Integer[] order=new Integer[count];for(int i=0;i<count;i++)order[i]=i;final double[] remaining=residual;Arrays.sort(order,(a,b)->Double.compare(Math.abs(remaining[b]),Math.abs(remaining[a])));ArrayList<Integer> centers=new ArrayList<>();
   for(int index:order){boolean separate=true;for(int other:centers)if(Math.abs(Math.log(hz[index]/hz[other])/Math.log(2))<=.2)separate=false;if(separate)centers.add(index);if(centers.size()==5)break;}
   Preset.Band best=null;double[] bestResponse=null,bestResidual=null;double bestScore=score(residual);
   for(int center:centers){
    ArrayList<String> types=new ArrayList<>();types.add("peak");if(center<12&&o.min<=25)types.add("low_shelf");if(center>count-13&&o.max>=16000)types.add("high_shelf");
    for(String type:types)for(double q:new double[]{.3,.5,.707,1,1.5,2,3})if(q<=o.q&&(hz[center]<=6000||q<=1.5))for(double fraction:new double[]{.65,1}){
     Preset.Band trial=band(type,hz[center],clamp(residual[center]*fraction,-o.cut,o.boost),q);if(Math.abs(trial.gain)<.05)continue;double[] r=response(trial,rate,hz),next=new double[count];for(int i=0;i<count;i++)next[i]=residual[i]-r[i];if(!bounded(desired,next,o))continue;double value=score(next);if(value<bestScore-1e-6){bestScore=value;best=trial;bestResponse=r;bestResidual=next;}
    }
   }
   if(best==null)break;bands.add(best);contributions.add(bestResponse);residual=bestResidual;
  }
  for(int pass=0;pass<2;pass++)for(int n=0;n<bands.size();n++){
   Preset.Band currentBand=bands.get(n),best=currentBand;double bestScore=score(residual);double[] bestResponse=contributions.get(n),bestResidual=residual;
   double f=currentBand.hz,g=currentBand.gain,q=currentBand.q;
   for(double[] v:new double[][]{{f,g+.2,q},{f,g-.2,q},{f*.94,g,q},{f*1.06,g,q},{f,g,q*.85},{f,g,q*1.15}}){
    if(v[0]<o.min||v[0]>o.max||v[1]>o.boost||v[1]< -o.cut||v[2]<.3||v[2]>o.q||(v[0]>6000&&v[2]>1.5))continue;
    Preset.Band trial=band(currentBand.type,v[0],v[1],round(v[2],3));double[] r=response(trial,rate,hz),next=new double[count];for(int i=0;i<count;i++)next[i]=residual[i]+contributions.get(n)[i]-r[i];if(!bounded(desired,next,o))continue;double value=score(next);if(value<bestScore-1e-6){bestScore=value;best=trial;bestResponse=r;bestResidual=next;}
   }bands.set(n,best);contributions.set(n,bestResponse);residual=bestResidual;
  }
  double sum=0;result.response=new double[count][5];for(int i=0;i<count;i++){double eq=desired[i]-residual[i],error=raw[i]-eq;sum+=error*error;result.maximum=Math.max(result.maximum,Math.abs(error));result.response[i]=new double[]{hz[i],ts[i],ms[i],eq,ms[i]+eq};}
  result.before=Math.sqrt(score(raw)/count);result.after=Math.sqrt(sum/count);result.preset=current.copy();result.preset.name="→ "+target.name;result.preset.enabled=true;result.preset.bands.clear();result.preset.bands.addAll(bands);return result;
 }
 static void coverage(double[][] points,Options o)throws Exception{if(points[0][0]>Math.min(o.min,1000)||points[points.length-1][0]<Math.max(o.max,1000))throw new Exception("数据未覆盖拟合范围和1kHz对齐点，不外推");}
}
