using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Point=System.Windows.Point;
using MouseEventArgs=System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs=System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs=System.Windows.Input.MouseWheelEventArgs;
using Pen=System.Windows.Media.Pen;
using Color=System.Windows.Media.Color;
using ColorConverter=System.Windows.Media.ColorConverter;
using FontFamily=System.Windows.Media.FontFamily;
using FlowDirection=System.Windows.FlowDirection;
namespace OpenEQ;
/// <summary>Hardware-style rotary knob: drag vertically to change, arc shows value.</summary>
public sealed class Knob:FrameworkElement {
 public event Action<double>? Changed;
 public double Minimum{get;set;}=0;public double Maximum{get;set;}=100;public int Decimals{get;set;}=1;
 public string Caption{get;set;}="";public string Unit{get;set;}="";
 double value;bool dragging;double startY,startValue;
 public double Value{get=>value;set{double next=Math.Clamp(value,Minimum,Maximum);if(this.value==next)return;this.value=next;InvalidateVisual();}}
 public Knob(){Cursor=System.Windows.Input.Cursors.SizeAll;}
 public void SetSilently(double v)=>Value=v;
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);
  double size=Math.Min(ActualWidth,ActualHeight-22);if(size<20)return;
  var center=new Point(ActualWidth/2,size/2+2);double r=size/2-7;
  double frac=(value-Minimum)/(Maximum-Minimum);double start=135,sweep=270;
  dc.DrawGeometry(null,new Pen(Solid("#DCE3F1"),7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Arc(center,r,start,sweep));
  if(frac>0)dc.DrawGeometry(null,new Pen(Solid("#586FF4"),7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Arc(center,r,start,sweep*frac));
  double ang=(start+sweep*frac)*Math.PI/180;dc.DrawEllipse(Solid("#FFFFFF"),new Pen(Solid("#8F9FFA"),2),new Point(center.X+Math.Cos(ang)*(r-12),center.Y+Math.Sin(ang)*(r-12)),4,4);
  dc.DrawEllipse(Solid("#F2F5FC"),null,center,r-14,r-14);
  string text=value.ToString("0."+new string('0',Decimals));var tf=new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
  dc.DrawText(new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,tf,13,Solid("#2B3957"),1.25),new Point(center.X-18,center.Y-9));
  if(Caption!=""){var cf=new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);dc.DrawText(new FormattedText(Caption+(Unit!=""?" "+Unit:""),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,cf,10,Solid("#7D8AA2"),1.25),new Point(Math.Max(0,ActualWidth/2-42),size+6));}
 }
 static Geometry Arc(Point c,double r,double startDeg,double sweepDeg){var g=new StreamGeometry();using(var ctx=g.Open()){ctx.BeginFigure(new Point(c.X+Math.Cos(startDeg*Math.PI/180)*r,c.Y+Math.Sin(startDeg*Math.PI/180)*r),false,false);int n=Math.Max(2,(int)(sweepDeg/8));for(int i=1;i<=n;i++){double a=(startDeg+sweepDeg*i/n)*Math.PI/180;ctx.LineTo(new Point(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r),true,false);}}g.Freeze();return g;}
 static SolidColorBrush Solid(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
 protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){base.OnMouseLeftButtonDown(e);dragging=true;startY=e.GetPosition(this).Y;startValue=value;CaptureMouse();e.Handled=true;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging)return;double delta=(startY-e.GetPosition(this).Y)*(Maximum-Minimum)/200;double previous=value;Value=startValue+delta;if(value!=previous)Changed?.Invoke(value);}
 protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);if(dragging){dragging=false;ReleaseMouseCapture();Changed?.Invoke(value);}}
 protected override void OnMouseWheel(MouseWheelEventArgs e){base.OnMouseWheel(e);double step=(Maximum-Minimum)/100,previous=value;Value=value+(e.Delta>0?step:-step);if(value!=previous)Changed?.Invoke(value);e.Handled=true;}
}
/// <summary>Horizontal segmented level meter (dB scale, -60..0).</summary>
public sealed class VuMeter:FrameworkElement {
 public double Db{get;set;}=-60;public string Caption{get;set;}="";
 protected override void OnRender(DrawingContext dc){base.OnRender(dc);double h=10,w=ActualWidth;if(w<20)return;
  var bg=new Rect(0,12,w,h);dc.DrawRoundedRectangle(Solid("#E4E9F4"),null,bg,5,5);
  double frac=Math.Clamp((Db+60)/60,0,1);if(frac>0){var fg=new Rect(0,12,Math.Max(6,w*frac),h);
   dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(88,111,244),Color.FromRgb(49,189,179),0),null,fg,5,5);}
  for(int i=1;i<6;i++){double x=w*i/6;dc.DrawLine(new Pen(Solid("#FFFFFF"),1),new Point(x,12),new Point(x,12+h));}
  if(Caption!=""){var tf=new Typeface(new FontFamily("Microsoft YaHei UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);dc.DrawText(new FormattedText(Caption,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,tf,9,Solid("#7D8AA2"),1.25),new Point(0,0));}
 }
 static SolidColorBrush Solid(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
}
