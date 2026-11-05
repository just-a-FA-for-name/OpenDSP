using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using Cursors=System.Windows.Input.Cursors;
namespace OpenEQ;

public partial class ModernWindow {
 List<CurvePoint>? measuredProfile;
 string measurementSource="";
 int tuningContextVersion;
 bool fittingTarget;
 readonly List<TargetCurves.Curve> importedTargets=new();
 string SelectedMeasurementRig()=>(MeasurementRig?.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"none";
 void ResetRackClick(object sender,RoutedEventArgs e){
  try{HandleControlCore(JsonSerializer.Serialize(new{cmd="reset_output",section=((Button)sender).Tag.ToString()!,expected_revision=AiTuning.Revision(preset,ControlContext)}));SetStatus("默认值已恢复","当前模块已恢复默认；可撤销，EQ与输出格式保持。");}
  catch(Exception ex){SetStatus("未能恢复",ex.Message,true);}
 }
 void CurveFilterChanged(object sender,SelectionChangedEventArgs e){if(CurveList!=null)BuildCurveCards();}
 void CurveSearchChanged(object sender,TextChangedEventArgs e){if(CurveList!=null)BuildCurveCards();}
 void CurveRigChanged(object sender,SelectionChangedEventArgs e){tuningContextVersion++;UpdateMeasurementStatus();}
 void UpdateMeasurementStatus(){if(MeasurementStatus==null)return;MeasurementStatus.Text=measuredProfile==null?
  "声学目标需同体系原始耳机频响；听感偏好是相对EQ。先预览，再应用。AI可读取导入的数据并生成方案。":
  $"已导入 {Path.GetFileName(measurementSource)} · {measuredProfile.Count} 点 · {SelectedMeasurementRig()} · 1kHz对齐后拟合。";}
 void BuildCurveCards(){
  if(CurveList==null)return;CurveList.Children.Clear();
  string search=CurveSearch?.Text.Trim()??"";int category=CurveCategory?.SelectedIndex??0;
  foreach(var curve in TargetCurves.BuiltIn.Concat(importedTargets)){
   if(category==1&&!curve.RequiresMeasurement||category==2&&curve.RequiresMeasurement)continue;
   if(search.Length>0&&!($"{curve.Name} {curve.Description} {curve.Rig}").Contains(search,StringComparison.OrdinalIgnoreCase))continue;
   var card=new Border{Width=330,Background=Brush("#F7F9FD"),CornerRadius=new CornerRadius(10),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,12,10),Cursor=Cursors.Hand,ToolTip=curve.Source};
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(102)});grid.ColumnDefinitions.Add(new ColumnDefinition());
   var thumbnail=new CurvePreview{Width=94,Height=52,Shape=curve.Response,VerticalAlignment=VerticalAlignment.Center};
   var text=new StackPanel{Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
   text.Children.Add(new TextBlock{Text=curve.Name,FontWeight=FontWeights.SemiBold,FontSize=11,TextWrapping=TextWrapping.Wrap});
   text.Children.Add(new TextBlock{Text=(curve.RequiresMeasurement?"测量目标 · "+curve.Rig:"听感偏好 · 相对EQ")+"\n"+curve.Description,FontSize=9,Foreground=Brush("#8B97AE"),TextWrapping=TextWrapping.Wrap});
   Grid.SetColumn(text,1);grid.Children.Add(thumbnail);grid.Children.Add(text);card.Child=grid;
   card.MouseLeftButtonDown+=(_,_)=>PreviewTarget(curve);CurveList.Children.Add(card);
  }
 }
 JsonElement GuiTargetRequest(TargetCurves.Curve target){
  var request=new Dictionary<string,object?>{
   ["band_count"]=int.Parse(((ComboBoxItem)FitBands.SelectedItem).Content.ToString()!),
   ["max_hz"]=Math.Min(double.Parse(((ComboBoxItem)FitMaxHz.SelectedItem).Content.ToString()!),ControlSampleRate*.49),
   ["auto_headroom"]=false,["measurement_rig"]=SelectedMeasurementRig()
  };
  if(importedTargets.Contains(target)){
   request["target_points"]=Enumerable.Range(0,target.Points.GetLength(0)).Select(i=>new[]{target.Points[i,0],target.Points[i,1]}).ToArray();
   request["target_name"]=target.Name;request["target_kind"]=target.Kind;request["target_rig"]=target.Rig;
  }else request["target_id"]=target.Id;
  return JsonSerializer.SerializeToElement(request);
 }
 async void PreviewTarget(TargetCurves.Curve target){
  if(fittingTarget)return;fittingTarget=true;CurveList.IsEnabled=false;
  try{
   var current=preset.Copy();int rate=ControlSampleRate;string context=ControlContext,rig=SelectedMeasurementRig();
   var measured=measuredProfile?.ToList();var request=GuiTargetRequest(target);
   SetStatus("正在计算目标拟合","可继续操作；生成方案不会自动改变声音。");
   var proposal=await Task.Run(()=>TargetWorkflow.Build(request,current,rate,context,measured,rig));
   if(closed)return;
   var d=Dialog("预览 · "+target.Name,740,640);var panel=(StackPanel)d.Content;
   panel.Children.Add(new TextBlock{Text=$"{proposal.Fit.Bands.Count} 段 · {rate:N0} Hz · 前级 {proposal.Preview.Preset.Preamp:0.#} dB\n形状误差 RMS：{proposal.Fit.BeforeRmsDb:0.00} → {proposal.Fit.AfterRmsDb:0.00} dB\n{proposal.Fit.Options.MinHz:0}–{proposal.Fit.Options.MaxHz:0} Hz · 超出提升/衰减限值的频点：{proposal.Fit.ClippedPoints}\n{proposal.Fit.Scope}\n峰值保护可能介入，实际听感与匹配程度仍需试听或重新测量。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,8)});
   var correction=Correction(proposal);
   panel.Children.Add(new EqGraph{Height=230,Preset=proposal.Preview.Preset,SampleRate=rate,Target=hz=>TargetCurves.Interpolate(correction,hz),IsHitTestVisible=false});
   panel.Children.Add(new TextBlock{Text="橙线是所需EQ修正，蓝/绿线为拟合EQ；不是耳机声学实测曲线。",FontSize=11,TextWrapping=TextWrapping.Wrap});
   panel.Children.Add(DialogButton("应用此方案（可撤销）",()=>{
    try{ApplyTargetProposal(proposal);SetStatus("调音方案已应用",target.Name+" · 请结合实听确认。");}
    catch(Exception ex){SetStatus("方案未应用",ex.Message,true);}
    d.Close();
   }));
   panel.Children.Add(DialogButton("取消，保留当前声音",d.Close));d.ShowDialog();
  }catch(Exception ex){SetStatus("目标预览未完成",ex.Message,true);}
  finally{fittingTarget=false;if(!closed)CurveList.IsEnabled=true;}
 }
 static List<CurvePoint> Correction(TargetProposal proposal)=>proposal.Fit.Response.Select(row=>new CurvePoint(row[0],row[1]-row[2]+proposal.Preview.Preset.Preamp)).ToList();
 void SetTargetOverlay(TargetProposal proposal){var correction=Correction(proposal);Graph.Target=hz=>TargetCurves.Interpolate(correction,hz);Graph.InvalidateVisual();}
 string ApplyTargetProposal(TargetProposal proposal){
  string result=HandleControlCore(JsonSerializer.Serialize(new{cmd="apply_preset",preset=proposal.Preview.Preset,expected_revision=proposal.Preview.BaseRevision,auto_headroom=false}));
  SetTargetOverlay(proposal);return result;
 }
 void TuneImportClick(object sender,RoutedEventArgs e){
  var dialog=new Microsoft.Win32.OpenFileDialog{Filter="原始耳机频响 CSV/TXT|*.csv;*.txt"};
  if(dialog.ShowDialog(this)!=true)return;
  try{measuredProfile=TargetCurves.ParseMeasured(File.ReadAllText(dialog.FileName));measurementSource=dialog.FileName;tuningContextVersion++;UpdateMeasurementStatus();}
  catch(Exception ex){SetStatus("频响导入失败",ex.Message,true);}
 }
 void TargetImportClick(object sender,RoutedEventArgs e){
  if(SelectedMeasurementRig()=="none"){SetStatus("请选择测量体系","导入目标前，请选择该目标使用的测量体系。",true);return;}
  var dialog=new Microsoft.Win32.OpenFileDialog{Filter="声学目标 CSV/TXT|*.csv;*.txt"};
  if(dialog.ShowDialog(this)!=true)return;
  try{
   var points=TargetCurves.ParseMeasured(File.ReadAllText(dialog.FileName));
   string name=Path.GetFileNameWithoutExtension(dialog.FileName);if(name.Length>80)name=name[..80];
   importedTargets.Add(new(name,"本次会话导入的声学目标",TargetCurves.ToArray(points),"custom-"+importedTargets.Count,"acoustic",SelectedMeasurementRig(),"custom",dialog.FileName));
   BuildCurveCards();SetStatus("自定义目标已导入","可点击卡片预览；目标与耳机频响必须来自同一测量体系。");
  }catch(Exception ex){SetStatus("目标导入失败",ex.Message,true);}
 }
 void TuneClearClick(object sender,RoutedEventArgs e){measuredProfile=null;measurementSource="";tuningContextVersion++;Graph.Target=null;Graph.InvalidateVisual();UpdateMeasurementStatus();SetStatus("测量已清除","当前EQ保持，尚未应用的测量方案已失效。");}
 string HandleTargetRequest(JsonElement request,bool apply){
  var snapshot=Dispatcher.Invoke(()=>(Preset:preset.Copy(),Rate:ControlSampleRate,Context:ControlContext,Measured:measuredProfile?.ToList(),Rig:SelectedMeasurementRig()));
  string? expected=apply?request.GetProperty("expected_revision").GetString():null;
  if(apply)AiTuning.RequireRevision(expected!,snapshot.Preset,snapshot.Context);
  if(apply){var fields=request.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone());fields["target_id"]=request.GetProperty("name").GetString();fields.Remove("name");request=JsonSerializer.SerializeToElement(fields);}
  var proposal=TargetWorkflow.Build(request,snapshot.Preset,snapshot.Rate,snapshot.Context,snapshot.Measured,snapshot.Rig);
  if(!apply)return JsonSerializer.Serialize(proposal.Payload());
  return Dispatcher.Invoke(()=>{
   AiTuning.RequireRevision(expected!,preset,ControlContext);return ApplyTargetProposal(proposal);
  });
 }
}
