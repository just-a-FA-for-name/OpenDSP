using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using Color=System.Windows.Media.Color;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;

namespace OpenEQ;
public sealed class NumberEditor:System.Windows.Controls.UserControl {
 public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(NumberEditor),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,(d,e)=>((NumberEditor)d).Refresh()));
 public static readonly DependencyProperty MinimumProperty=DependencyProperty.Register(nameof(Minimum),typeof(double),typeof(NumberEditor),new PropertyMetadata(-100.0));
 public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(double),typeof(NumberEditor),new PropertyMetadata(100.0));
 public static readonly DependencyProperty DecimalsProperty=DependencyProperty.Register(nameof(Decimals),typeof(int),typeof(NumberEditor),new PropertyMetadata(1,(d,e)=>((NumberEditor)d).Refresh()));
 public double Value{get=>(double)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}public double Minimum{get=>(double)GetValue(MinimumProperty);set=>SetValue(MinimumProperty,value);}public double Maximum{get=>(double)GetValue(MaximumProperty);set=>SetValue(MaximumProperty,value);}public int Decimals{get=>(int)GetValue(DecimalsProperty);set=>SetValue(DecimalsProperty,value);}
 readonly TextBox box;public event Action<double>? Changed;
 public NumberEditor(){var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(32)});
  var border=new Border{Background=new SolidColorBrush(Color.FromRgb(245,247,252)),CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(229,234,244)),MinHeight=36,Child=row};Content=border;
  box=new TextBox{Text="0.0",BorderThickness=new Thickness(0),Background=Brushes.Transparent,VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,FontSize=15,Padding=new Thickness(0,6,0,6),MinWidth=30};Grid.SetColumn(box,1);row.Children.Add(box);
  Button Add(string text,int col,int direction){var b=new Button{Content=text,Background=Brushes.Transparent,Padding=new Thickness(0),BorderThickness=new Thickness(0),Foreground=new SolidColorBrush(Color.FromRgb(136,150,177)),FontSize=17,Focusable=false};b.Click+=(s,e)=>Step(direction);Grid.SetColumn(b,col);row.Children.Add(b);return b;}Add("−",0,-1);Add("+",2,1);
  box.GotKeyboardFocus+=(s,e)=>box.SelectAll();box.LostKeyboardFocus+=(s,e)=>Commit();box.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}else if(e.Key==Key.Up){Step(1);e.Handled=true;}else if(e.Key==Key.Down){Step(-1);e.Handled=true;}};
 }
 void Refresh(){if(box!=null)box.Text=Value.ToString("F"+Decimals,CultureInfo.InvariantCulture);}
 public void SetSilently(double value){Value=value;Refresh();}
 public void Step(int direction){double value=Value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed)&&double.IsFinite(parsed))value=parsed;decimal step=(decimal)Math.Pow(10,-Math.Clamp(Decimals,0,6));decimal next=(decimal)value+direction*step;Accept((double)Math.Clamp(next,(decimal)Minimum,(decimal)Maximum));}
 void Commit(){if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<Minimum||value>Maximum){box.ToolTip=$"请输入 {Minimum}–{Maximum}";Refresh();return;}Accept(value);}
 void Accept(double value){if(!double.IsFinite(value))return;value=Math.Clamp(value,Minimum,Maximum);bool changed=Math.Abs(value-Value)>1e-12;Value=value;Refresh();if(changed)Changed?.Invoke(value);}
}
