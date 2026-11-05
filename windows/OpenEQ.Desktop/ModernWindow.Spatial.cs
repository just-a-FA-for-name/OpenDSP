using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using Orientation=System.Windows.Controls.Orientation;
using Binding=System.Windows.Data.Binding;
namespace OpenEQ;

public partial class ModernWindow {
 bool ApplyUserPreset(Preset candidate){
  try{
   if(nativeApo!=null)NativeApo.EnsureSupported(candidate);
   HandleControlCore(JsonSerializer.Serialize(new{cmd="apply_preset",preset=candidate,expected_revision=AiTuning.Revision(preset,ControlContext),auto_headroom=false}));
   selected=0;RefreshTiles();LoadSelected();return true;
  }catch(Exception ex){RefreshPreset(false);bool previous=updating;updating=true;PresetPick.SelectedItem=preset.Name;updating=previous;SetStatus("预设未应用",ex.Message,true);return false;}
 }
 void InitSpatialFields(){
  SpatialScenePick.ItemsSource=SpatialScenes.All;SpatialScenePick.SelectedIndex=0;
  SpatialMix.Changed+=v=>EditSpatial(s=>s.Mix=v/100);SpatialMidGain.Changed+=v=>EditSpatial(s=>s.MidGain=v);SpatialSideGain.Changed+=v=>EditSpatial(s=>s.SideGain=v);
  SpatialLowWidth.Changed+=v=>EditSpatial(s=>s.LowWidth=v/100);SpatialMidWidth.Changed+=v=>EditSpatial(s=>s.MidWidth=v/100);SpatialHighWidth.Changed+=v=>EditSpatial(s=>s.HighWidth=v/100);
  SpatialLowCross.Changed+=v=>EditSpatial(s=>s.LowCrossover=v);SpatialHighCross.Changed+=v=>EditSpatial(s=>s.HighCrossover=v);
  SpatialCrossfeed.Changed+=v=>EditSpatial(s=>s.Crossfeed=v/100);SpatialCrossfeedHz.Changed+=v=>EditSpatial(s=>s.CrossfeedHz=v);SpatialCrossfeedDelay.Changed+=v=>EditSpatial(s=>s.CrossfeedDelay=v);
 }
 void SyncSpatial(){
  if(SpatialEnabled==null)return;var s=preset.Output.Spatial??new SpatialConfig();
  SpatialEnabled.IsChecked=s.Enabled;SpatialMix.SetSilently(s.Mix*100);SpatialMidGain.SetSilently(s.MidGain);SpatialSideGain.SetSilently(s.SideGain);
  SpatialLowWidth.SetSilently(s.LowWidth*100);SpatialMidWidth.SetSilently(s.MidWidth*100);SpatialHighWidth.SetSilently(s.HighWidth*100);
  SpatialLowCross.SetSilently(s.LowCrossover);SpatialHighCross.SetSilently(s.HighCrossover);
  SpatialCrossfeed.SetSilently(s.Crossfeed*100);SpatialCrossfeedHz.SetSilently(s.CrossfeedHz);SpatialCrossfeedDelay.SetSilently(s.CrossfeedDelay);SpatialMono.IsChecked=s.Mono;
  SpatialBankStatus.Text=$"中央 {s.MidBands.Count}/16 段 · 侧向 {s.SideBands.Count}/16 段";
 }
 void ApplySpatial(SpatialConfig config,string revision){
  HandleControlCore(JsonSerializer.Serialize(new{cmd="set_spatial",spatial=config,expected_revision=revision}));
 }
 void EditSpatial(Action<SpatialConfig> edit){
  if(updating)return;
  try{var next=preset.Copy().Output.Spatial??new SpatialConfig();edit(next);ApplySpatial(next,AiTuning.Revision(preset,ControlContext));}
  catch(Exception ex){RefreshPreset(false);SetStatus("空间参数未应用",ex.Message,true);}
 }
 void SpatialEnableClick(object sender,RoutedEventArgs e)=>EditSpatial(s=>s.Enabled=SpatialEnabled.IsChecked==true);
 void SpatialMonoClick(object sender,RoutedEventArgs e)=>EditSpatial(s=>s.Mono=SpatialMono.IsChecked==true);
 void SpatialBypassClick(object sender,RoutedEventArgs e)=>EditSpatial(s=>s.Enabled=false);
 void SpatialSceneClick(object sender,RoutedEventArgs e){
  if(SpatialScenePick.SelectedItem is not SpatialScenes.Scene scene)return;
  try{ApplySpatial(SpatialScenes.Create(scene.Id),AiTuning.Revision(preset,ControlContext));SetStatus("空间场景已应用",scene.Name+" · "+scene.Description+" 可旁路或撤销。");}
  catch(Exception ex){SetStatus("场景未应用",ex.Message,true);}
 }
 void MidSideEditorClick(object sender,RoutedEventArgs e){
  var spatial=preset.Copy().Output.Spatial??new SpatialConfig();string revision=AiTuning.Revision(preset,ControlContext);int rate=ControlSampleRate;
  var mid=new ObservableCollection<Band>(spatial.MidBands);var side=new ObservableCollection<Band>(spatial.SideBands);
  var dialog=Dialog("中央 / 侧向 EQ",1000,880);dialog.MaxHeight=SystemParameters.WorkArea.Height-20;
  var panel=(StackPanel)dialog.Content;dialog.Content=null;dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  panel.Children.Add(new TextBlock{Text="M=(L+R)/2，S=(L−R)/2。中央常包含居中的人声，但不是人声分离；侧向常含两侧伴奏与空间信息。各最多16段；增益仅用于峰值/架式滤波，通滤波/陷波/全通不使用增益。保存后由空间总开关决定是否生效。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)});
  DataGrid Bank(string title,ObservableCollection<Band> bands){
   panel.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,10,0,6)});
   var grid=new DataGrid{ItemsSource=bands,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,Height=170,RowHeaderWidth=0,SelectionMode=DataGridSelectionMode.Single};
   grid.Columns.Add(new DataGridCheckBoxColumn{Header="启用",Binding=new Binding(nameof(Band.Enabled))});
   grid.Columns.Add(new DataGridComboBoxColumn{Header="类型",ItemsSource=Preset.Types.Select((id,i)=>new{Id=id,Label=Preset.Labels[i]}).ToArray(),SelectedValuePath="Id",DisplayMemberPath="Label",SelectedValueBinding=new Binding(nameof(Band.Type)),Width=160});
   foreach(var item in new[]{("频率 Hz",nameof(Band.Frequency)),("增益 dB",nameof(Band.Gain)),("Q",nameof(Band.Q))})
    grid.Columns.Add(new DataGridTextColumn{Header=item.Item1,Width=170,Binding=new Binding(item.Item2){ValidatesOnExceptions=true,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus}});
   panel.Children.Add(grid);var buttons=new StackPanel{Orientation=Orientation.Horizontal};
   var add=new Button{Content="添加频段",Margin=new Thickness(0,6,8,0)};add.Click+=(_,_)=>{if(bands.Count<16)bands.Add(new Band());};buttons.Children.Add(add);
   var remove=new Button{Content="移除选中",Margin=new Thickness(0,6,8,0)};remove.Click+=(_,_)=>{if(grid.SelectedItem is Band band)bands.Remove(band);};buttons.Children.Add(remove);
   var clear=new Button{Content="清空本组",Margin=new Thickness(0,6,8,0)};clear.Click+=(_,_)=>bands.Clear();buttons.Children.Add(clear);panel.Children.Add(buttons);return grid;
  }
  var midGrid=Bank("Mid · 中央信息",mid);var sideGrid=Bank("Side · 侧向信息",side);
  var error=new TextBlock{Foreground=Brush("#BC6B54"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};panel.Children.Add(error);
  var plotGrid=new Grid();plotGrid.ColumnDefinitions.Add(new ColumnDefinition());plotGrid.ColumnDefinitions.Add(new ColumnDefinition());
  var midPlot=new EqGraph{Height=180,SampleRate=rate,IsHitTestVisible=false};var sidePlot=new EqGraph{Height=180,SampleRate=rate,IsHitTestVisible=false};Grid.SetColumn(sidePlot,1);plotGrid.Children.Add(midPlot);plotGrid.Children.Add(sidePlot);panel.Children.Add(plotGrid);
  bool ValidateDraft(){
   try{
    if(!midGrid.CommitEdit(DataGridEditingUnit.Cell,true)||!midGrid.CommitEdit(DataGridEditingUnit.Row,true)||!sideGrid.CommitEdit(DataGridEditingUnit.Cell,true)||!sideGrid.CommitEdit(DataGridEditingUnit.Row,true))throw new ArgumentException("请修正表格中的无效数值");
    spatial.MidBands=mid.ToList();spatial.SideBands=side.ToList();spatial.Validate();
    midPlot.Preset=new Preset{Bands=mid.ToList()};sidePlot.Preset=new Preset{Bands=side.ToList()};midPlot.InvalidateVisual();sidePlot.InvalidateVisual();error.Text="";return true;
   }catch(Exception ex){error.Text=ex.Message;return false;}
  }
  panel.Children.Add(DialogButton("预览两组频响（左M / 右S）",()=>ValidateDraft()));
  panel.Children.Add(DialogButton("保存M/S参数（可撤销）",()=>{
   if(!ValidateDraft())return;
   try{ApplySpatial(spatial,revision);dialog.Close();SetStatus("M/S参数已保存",spatial.Enabled?"空间模块已启用。":"需要启用空间处理后生效。");}
   catch(Exception ex){error.Text=ex.Message;}
  }));
  panel.Children.Add(DialogButton("取消",dialog.Close));ValidateDraft();dialog.ShowDialog();
 }
}
