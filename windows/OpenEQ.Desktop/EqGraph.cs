using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Color=System.Windows.Media.Color;
using Point=System.Windows.Point;
using Pen=System.Windows.Media.Pen;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;
using ColorConverter=System.Windows.Media.ColorConverter;

namespace OpenEQ;
public sealed class EqGraph:FrameworkElement {
 public Preset Preset{get;set;}=Preset.Flat();public int SampleRate{get;set;}=48000;public int Selected{get;set;}=0;
 public Func<double,double>? Target{get;set;}
 public event Action<int>? SelectedChanged;public event Action<int,double,double>? NodeMoved;public event Action<int,int>? QStepped;public event Action<double,double>? AddRequested;public event Action? EditStarted;public event Action? EditFinished;
 bool dragging;readonly string[] colors={"#647AF8","#20B7AD","#B182DB","#F2A962","#E57C9F","#6FAEDD","#A0BA6E"};
 readonly EqPlotCache response=new();
 readonly Dictionary<string,Brush> brushes=new();
 readonly Dictionary<(string,double,bool),Pen> pens=new();
 readonly Dictionary<(string,string,double,double),FormattedText> textCache=new();
 static readonly Typeface Font=new("Microsoft YaHei UI");
 readonly LinearGradientBrush fillBrush=new(Color.FromArgb(28,92,116,248),Color.FromArgb(2,92,116,248),90);
 DrawingGroup? grid;StreamGeometry? leftCurve,rightCurve,area,targetCurve;
 Point[] nodes=Array.Empty<Point>();
 double geometryWidth=-1,geometryHeight=-1,gridWidth=-1,gridHeight=-1,gridDpi=-1;
 Func<double,double>? cachedTarget;
 internal int ResponseRebuilds=>response.Rebuilds;
 internal int GeometryRebuilds{get;private set;}
 internal int GridRebuilds{get;private set;}
 public EqGraph(){Cursor=System.Windows.Input.Cursors.Cross;Focusable=true;ClipToBounds=true;fillBrush.Freeze();}
 double Left=>38;double Right=>Math.Max(40,ActualWidth-15);double Top=>17;double Bottom=>Math.Max(35,ActualHeight-28);
 double X(double hz)=>Left+(Right-Left)*Math.Log10(hz/20)/3;double Y(double gain)=>Top+(Bottom-Top)*(18-gain)/36;
 double Frequency(double x)=>Math.Clamp(20*Math.Pow(1000,(x-Left)/(Right-Left)),20,20000);double Gain(double y)=>Math.Clamp(18-36*(y-Top)/(Bottom-Top),-24,24);
 public static double Snap(double value)=>Math.Round(value,1,MidpointRounding.AwayFromZero);
 Brush Solid(string hex){if(!brushes.TryGetValue(hex,out var brush)){var next=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));next.Freeze();brushes[hex]=brush=next;}return brush;}
 Pen Stroke(string hex,double width,bool dashed=false){var key=(hex,width,dashed);if(!pens.TryGetValue(key,out var pen)){pen=new Pen(Solid(hex),width);if(dashed)pen.DashStyle=DashStyles.Dash;pen.Freeze();pens[key]=pen;}return pen;}
 void Text(DrawingContext dc,string text,double x,double y,string color,double size=10){
  var key=(text,color,size,VisualTreeHelper.GetDpi(this).PixelsPerDip);
  if(!textCache.TryGetValue(key,out var ft)){
   if(textCache.Count>=64)textCache.Clear(); // changing drag labels must not grow an unbounded cache
   ft=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,Font,size,Solid(color),key.Item4);textCache[key]=ft;
  }
  dc.DrawText(ft,new Point(x,y));
 }
 void EnsureGrid(){
  double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
  if(grid!=null&&gridWidth==ActualWidth&&gridHeight==ActualHeight&&gridDpi==dpi)return;
  gridWidth=ActualWidth;gridHeight=ActualHeight;gridDpi=dpi;grid=new DrawingGroup();
  using(var dc=grid.Open()){
   foreach(var db in new[]{-18,-12,-6,0,6,12,18}){dc.DrawLine(Stroke(db==0?"#DCE3F1":"#EFF2F8",1),new Point(Left,Y(db)),new Point(Right,Y(db)));Text(dc,(db>0?"+":"")+db,1,Y(db)-6,"#A1ADC2",9);}
   foreach(var hz in new[]{20,50,100,200,500,1000,2000,5000,10000,20000}){dc.DrawLine(Stroke("#F0F3F8",1),new Point(X(hz),Top),new Point(X(hz),Bottom));if(ActualWidth>650||hz is 20 or 100 or 1000 or 10000 or 20000)Text(dc,hz>=1000?hz/1000+"k":hz.ToString(),X(hz)-9,Bottom+10,"#A1ADC2",9);}
   Text(dc,"dB",1,0,"#A1ADC2",9);
  }
  grid.Freeze();GridRebuilds++;
 }
 StreamGeometry Geometry(Point[] points,bool fill=false){
  var geometry=new StreamGeometry();
  using(var context=geometry.Open()){
   context.BeginFigure(fill?new Point(Left,Bottom):points[0],fill,fill);
   context.PolyLineTo(points,true,false);
   if(fill)context.LineTo(new Point(Right,Bottom),true,false);
  }
  geometry.Freeze();return geometry;
 }
 void EnsureCurves(){
  bool changed=response.Update(Preset,SampleRate);
  bool resized=geometryWidth!=ActualWidth||geometryHeight!=ActualHeight;
  if(changed||resized||leftCurve==null){
   geometryWidth=ActualWidth;geometryHeight=ActualHeight;
   var left=new Point[EqPlotCache.Frequencies.Length];var right=new Point[left.Length];
   for(int i=0;i<left.Length;i++){
    double x=Left+(Right-Left)*i/(left.Length-1);
    left[i]=new Point(x,Math.Clamp(Y(response.LeftDb[i]),Top-8,Bottom+4));
    right[i]=new Point(x,Math.Clamp(Y(response.RightDb[i]),Top-8,Bottom+4));
   }
   leftCurve=Geometry(left);rightCurve=Geometry(right);area=Geometry(left,true);
   if(nodes.Length!=Preset.Bands.Count)nodes=new Point[Preset.Bands.Count];
   for(int i=0;i<nodes.Length;i++)nodes[i]=new Point(X(Preset.Bands[i].Frequency),Math.Clamp(Y(response.NodeDb[i]),Top-8,Bottom+4));
   GeometryRebuilds++;
  }
  if(resized||!ReferenceEquals(cachedTarget,Target)){
   cachedTarget=Target;targetCurve=null;
   if(Target!=null){var points=new Point[181];for(int i=0;i<points.Length;i++){double hz=20*Math.Pow(1000,i/180d);points[i]=new Point(X(hz),Math.Clamp(Y(Target(hz)),Top-8,Bottom+4));}targetCurve=Geometry(points);}
  }
 }
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(ActualWidth<80||ActualHeight<60)return;
  dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
  EnsureGrid();EnsureCurves();dc.DrawDrawing(grid);
  dc.DrawGeometry(null,Stroke("#31BDB3",2),rightCurve);
  dc.DrawGeometry(fillBrush,null,area);dc.DrawGeometry(null,Stroke("#5A71F6",2.5),leftCurve);
  if(targetCurve!=null){dc.DrawGeometry(null,Stroke("#E8A33D",1.6,true),targetCurve);Text(dc,"目标曲线",Math.Clamp(Right-64,Left,Right-60),Top+6,"#E8A33D",10);}
  for(int n=0;n<Preset.Bands.Count;n++){var b=Preset.Bands[n];var point=nodes[n];string hex=b.Enabled?colors[n%colors.Length]:"#BBC4D5";var color=Solid(hex);
   if(n==Selected){dc.DrawLine(Stroke("#DCE3FD",1,true),new Point(point.X,Top),new Point(point.X,Bottom));dc.DrawEllipse(Solid("#195A71F6"),null,point,17,17);}
   dc.DrawEllipse(Brushes.White,Stroke(hex,n==Selected?2.5:1.8),point,n==Selected?8:6,n==Selected?8:6);if(n==Selected)dc.DrawEllipse(color,null,point,3,3);
   if(n==Selected)Text(dc,(n+1).ToString("00")+" · "+b.Frequency.ToString("0.#")+" Hz",Math.Clamp(point.X-30,Left,Right-95),Math.Max(Top,point.Y-32),"#61729B",10);
  }
 }
 Point NodePoint(Band b){EnsureCurves();return nodes[Preset.Bands.IndexOf(b)];}
 int Hit(Point p){EnsureCurves();int hit=-1;double distance=22;for(int n=0;n<nodes.Length;n++){var d=(nodes[n]-p).Length;if(d<distance){distance=d;hit=n;}}return hit;}
 internal void RenderForChecks(DrawingContext context)=>OnRender(context);
 internal Point NodeForChecks(int index){EnsureCurves();return nodes[index];}
 protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);Focus();var point=e.GetPosition(this);int hit=Hit(point);if(hit>=0){Selected=hit;SelectedChanged?.Invoke(hit);EditStarted?.Invoke();dragging=true;CaptureMouse();InvalidateVisual();e.Handled=true;}else if(e.ClickCount==2&&point.X>=Left&&point.X<=Right&&point.Y>=Top&&point.Y<=Bottom){AddRequested?.Invoke(Snap(Frequency(point.X)),Snap(Gain(point.Y)));e.Handled=true;}}
 protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e){base.OnMouseMove(e);if(!dragging||Selected<0||Selected>=Preset.Bands.Count)return;var p=e.GetPosition(this);var b=Preset.Bands[Selected];double g=b.Gain;if(b.Type=="peak"||b.Type.EndsWith("shelf")){double currentY=NodePoint(b).Y;double delta=Gain(p.Y)-Gain(currentY);g=Snap(b.Gain+delta);}NodeMoved?.Invoke(Selected,Snap(Frequency(p.X)),g);InvalidateVisual();}
 protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);bool was=dragging;dragging=false;ReleaseMouseCapture();if(was)EditFinished?.Invoke();}
 protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e){bool was=dragging;dragging=false;base.OnLostMouseCapture(e);if(was)EditFinished?.Invoke();}
 protected override void OnMouseWheel(MouseWheelEventArgs e){base.OnMouseWheel(e);int n=Hit(e.GetPosition(this));if(n<0)n=Selected;if(n>=0&&n<Preset.Bands.Count){QStepped?.Invoke(n,e.Delta>0?1:-1);e.Handled=true;}}
}

public sealed class CurvePreview:FrameworkElement {
 public Func<double,double>? Shape{get;set;}
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(Shape==null||ActualWidth<10||ActualHeight<8)return;
  double top=2,bottom=ActualHeight-2;double Y(double db)=>top+(18-db)/36*(bottom-top);
  var pts=new List<Point>();for(int n=0;n<=48;n++){double hz=20*Math.Pow(1000,n/48.0);pts.Add(new Point(n/48.0*ActualWidth,Math.Clamp(Y(Shape(hz)),top,bottom)));}
  var geo=new StreamGeometry();using(var ctx=geo.Open()){ctx.BeginFigure(pts[0],false,false);ctx.PolyLineTo(pts.Skip(1).ToArray(),true,false);}geo.Freeze();
  dc.DrawLine(new Pen(Solid("#DCE3F1"),1),new Point(0,Y(0)),new Point(ActualWidth,Y(0)));
  dc.DrawGeometry(null,new Pen(Solid("#E8A33D"),1.4),geo);
 }
 static SolidColorBrush Solid(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
}
