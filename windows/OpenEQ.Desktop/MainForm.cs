using System.Drawing.Drawing2D;
using NAudio.CoreAudioApi;

namespace OpenEQ;
public sealed class MainForm:Form {
 readonly Color bg=Color.FromArgb(247,248,245),ink=Color.FromArgb(24,30,29),accent=Color.FromArgb(21,112,87);
 Preset preset=Preset.Flat();readonly List<string> history=new();readonly AudioEngine engine=new();
 readonly CheckBox exclusive=new(){Text="USB 独占输出",Checked=true,AutoSize=true,Font=new Font("Microsoft YaHei UI",16,FontStyle.Bold)};
 readonly CheckBox route=new(){Text="接管系统默认输出（停止时恢复）",Checked=true,AutoSize=true};
 readonly CheckBox enabled=new(){Text="启用参数 EQ",Checked=true,AutoSize=true};
 readonly ComboBox input=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill},output=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
 readonly ComboBox rate=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
 readonly ComboBox bits=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=65};
 readonly Label state=new(){AutoSize=true,Text="未启动 · 先选择虚拟音频入口与 USB 声卡"};
 readonly Label stats=new(){AutoSize=true,Text="输入 —   输出 —   缓冲 —"};
 readonly Label name=new(){AutoSize=true,Font=new Font("Microsoft YaHei UI",13,FontStyle.Bold)};
 readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BorderStyle=BorderStyle.None,BackgroundColor=Color.White};
 readonly NumericUpDown preamp=Number(-60,0,-6,1),frequency=Number(20,20000,1000,1),gain=Number(-24,24,0,1),q=Number(.1m,30,.707m,3);
 readonly ComboBox type=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=155},channel=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
 readonly CheckBox bandEnabled=new(){Text="启用此段",Checked=true,AutoSize=true};
 readonly Chart chart;readonly System.Windows.Forms.Timer timer=new(){Interval=400};
 bool loading;string? compareA,compareB;Button start=null!,stop=null!;readonly string saved;
 public MainForm(){
  Text="OpenEQ · Windows";BackColor=bg;ForeColor=ink;Font=new Font("Microsoft YaHei UI",10);MinimumSize=new Size(1060,850);Size=new Size(1180,950);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
  saved=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","desktop-preset.json");try{if(File.Exists(saved))preset=Preset.Parse(File.ReadAllText(saved));}catch{}
  chart=new Chart(()=>preset){Dock=DockStyle.Fill};
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,14,24,14),ColumnCount=1,RowCount=7};
  root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(var h in new float[]{40,182,46,165,56})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,100));Controls.Add(root);
  root.Controls.Add(new Label{Text="OpenEQ",Font=new Font("Microsoft YaHei UI",22,FontStyle.Bold),AutoSize=true},0,0);
  var usb=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(14,8,14,8),ColumnCount=4,RowCount=4};usb.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95));usb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));usb.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));usb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
  foreach(var height in new float[]{38,36,42,30})usb.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
  usb.Controls.Add(exclusive,0,0);usb.SetColumnSpan(exclusive,2);usb.Controls.Add(route,2,0);usb.SetColumnSpan(route,2);
  usb.Controls.Add(new Label{Text="音频入口",AutoSize=true,Anchor=AnchorStyles.Left},0,1);usb.Controls.Add(input,1,1);usb.Controls.Add(new Label{Text="USB 输出",AutoSize=true,Anchor=AnchorStyles.Left},2,1);usb.Controls.Add(output,3,1);
  var controls=Flow();start=Button("接管并开始",Start);stop=Button("停止并恢复",Stop);stop.Enabled=false;controls.Controls.Add(start);controls.Controls.Add(stop);controls.Controls.Add(Button("刷新设备",RefreshDevices));controls.Controls.Add(rate);controls.Controls.Add(Button("恢复异常路由",()=>{try{state.Text=DefaultRoute.Recover();}catch(Exception e){Error(e);}}));
  controls.Controls.Add(bits);usb.Controls.Add(controls,0,2);usb.SetColumnSpan(controls,4);usb.Controls.Add(state,0,3);usb.SetColumnSpan(state,4);root.Controls.Add(usb,0,1);
  var presetBar=Flow();presetBar.Controls.Add(name);presetBar.Controls.Add(Button("本机预设",LocalPresets));presetBar.Controls.Add(Button("导入",Import));presetBar.Controls.Add(Button("导出",Export));presetBar.Controls.Add(Button("重命名",Rename));presetBar.Controls.Add(Button("撤销",Undo));presetBar.Controls.Add(Button("A/B",Compare));root.Controls.Add(presetBar,0,2);
  root.Controls.Add(chart,0,3);
  var knobs=Flow();knobs.Controls.Add(enabled);knobs.Controls.Add(new Label{Text="前级 dB",AutoSize=true,Padding=new Padding(12,8,0,0)});knobs.Controls.Add(preamp);knobs.Controls.Add(Button("输出与声道",Advanced));knobs.Controls.Add(Button("＋ 添加滤波器",AddBand));knobs.Controls.Add(Button("删除选中段",DeleteBand));root.Controls.Add(knobs,0,4);
  foreach(var title in new[]{"段","类型","频率 Hz","增益 dB","Q","声道","状态"})grid.Columns.Add(title,title);grid.Columns[0].FillWeight=40;grid.Columns[1].FillWeight=150;grid.RowTemplate.Height=30;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(232,240,233);grid.DefaultCellStyle.SelectionBackColor=accent;grid.DefaultCellStyle.SelectionForeColor=Color.White;root.Controls.Add(grid,0,5);
  var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};footer.RowStyles.Add(new RowStyle(SizeType.Absolute,52));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,35));var editor=Flow();type.Items.AddRange(Preset.Labels);channel.Items.AddRange(new[]{"双声道","左声道","右声道"});
  editor.Controls.Add(type);AddLabel(editor,"Hz",frequency);AddLabel(editor,"dB",gain);AddLabel(editor,"Q",q);editor.Controls.Add(channel);editor.Controls.Add(bandEnabled);editor.Controls.Add(Button("应用此段",ApplyBand));footer.Controls.Add(editor,0,0);footer.Controls.Add(stats,0,1);root.Controls.Add(footer,0,6);
  rate.Items.AddRange(new object[]{44100,48000,96000,192000});rate.SelectedItem=48000;bits.Items.AddRange(new object[]{16,24,32});bits.SelectedItem=24;
  foreach(var combo in new[]{input,output,rate,bits,type,channel}){combo.DrawMode=DrawMode.OwnerDrawFixed;combo.DrawItem+=(s,e)=>{if(e.Index<0)return;e.DrawBackground();TextRenderer.DrawText(e.Graphics,combo.Items[e.Index]?.ToString()??"",combo.Font,e.Bounds,e.ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);e.DrawFocusRectangle();};}
  enabled.CheckedChanged+=(s,e)=>{if(loading)return;Remember();preset.Enabled=enabled.Checked;Changed();};preamp.ValueChanged+=(s,e)=>{if(loading)return;Remember();preset.Preamp=(double)preamp.Value;Changed();};grid.SelectionChanged+=(s,e)=>LoadEditor();
  engine.Failed+=message=>{if(!IsDisposed)BeginInvoke(new Action(()=>{Stop();state.Text="已停止并恢复路由："+message;}));};
  FormClosing+=(s,e)=>{timer.Stop();Stop();};timer.Tick+=(s,e)=>{if(engine.Running)stats.Text=$"输入 {Db(engine.PeakIn):F1} dBFS    输出 {Db(engine.PeakOut):F1} dBFS    缓冲 {engine.BufferedMilliseconds:F0} ms    同步 {engine.ClockPpm:F0} ppm    削波 {engine.Clipped}    空读 {engine.StarvedReads}    溢出 {engine.OverflowEvents}";};timer.Start();RefreshDevices();Reload();
 }
 static double Db(double x)=>20*Math.Log10(Math.Max(1e-12,x));
 public void CheckUiDefaults(){if(input.SelectedItem is not DeviceOption||output.SelectedItem is not DeviceOption||type.SelectedIndex<0||channel.SelectedIndex<0||rate.SelectedItem==null||bits.SelectedItem==null)throw new Exception("默认设备或编辑器选择为空");foreach(var number in new[]{preamp,frequency,gain,q})if(number.Increment!=.1m)throw new Exception("数值步进不是 0.1");}
 public string DescribeUi()=>System.Text.Json.JsonSerializer.Serialize(new{input=input.SelectedItem?.ToString(),output=output.SelectedItem?.ToString(),rate=rate.SelectedItem,bits=bits.SelectedItem,type=type.SelectedItem,channel=channel.SelectedItem,increment=frequency.Increment,format="WinForms component render; not a live desktop screenshot"});
 static NumericUpDown Number(decimal min,decimal max,decimal value,int decimals)=>new(){Minimum=min,Maximum=max,Value=value,DecimalPlaces=decimals,Increment=.1m,Width=100,ThousandsSeparator=false};
 FlowLayoutPanel Flow()=>new(){Dock=DockStyle.Fill,WrapContents=false,AutoSize=false,Padding=new Padding(0,4,0,0)};
 Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(232,240,233),ForeColor=accent,Margin=new Padding(3,1,5,1)};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();return b;}
 static void AddLabel(FlowLayoutPanel row,string label,Control c){row.Controls.Add(new Label{Text=label,AutoSize=true,Padding=new Padding(3,6,0,0)});row.Controls.Add(c);}
 void RefreshDevices(){if(engine.Running)return;var devices=AudioEngine.Devices();input.Items.Clear();output.Items.Clear();foreach(var d in devices){input.Items.Add(d);output.Items.Add(d);}var virtualDevice=devices.FirstOrDefault(d=>d.Name.Contains("Steam Streaming Speakers"))??devices.FirstOrDefault(d=>d.Name.Contains("CABLE")||d.Name.Contains("虚拟"));input.SelectedItem=virtualDevice;output.SelectedItem=devices.FirstOrDefault(d=>d.Name.Contains("Tears DSP",StringComparison.OrdinalIgnoreCase));if(output.SelectedIndex<0&&output.Items.Count>0)output.SelectedIndex=0;}
 void Start(){try{if(input.SelectedItem is not DeviceOption src||output.SelectedItem is not DeviceOption dst)throw new Exception("请选择虚拟入口和 USB 输出");engine.LastError=null;engine.Start(src.Id,dst.Id,preset,(int)rate.SelectedItem!,exclusive.Checked,route.Checked,(int)bits.SelectedItem!);state.Text="运行中 · "+engine.FormatLabel;start.Enabled=false;stop.Enabled=true;input.Enabled=output.Enabled=rate.Enabled=bits.Enabled=exclusive.Enabled=route.Enabled=false;}catch(Exception e){Error(e);}}
 void Stop(){engine.Dispose();start.Enabled=true;stop.Enabled=false;input.Enabled=output.Enabled=rate.Enabled=bits.Enabled=exclusive.Enabled=route.Enabled=true;state.Text="已停止 · 已尝试恢复原默认输出";}
 void Remember(){if(history.Count>=100)history.RemoveAt(0);history.Add(preset.Encode());}
 void Changed(){preset.Validate();Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.WriteAllText(saved,preset.Encode());engine.Update(preset);chart.Invalidate();name.Text=preset.Name;}
 void Reload(){int selected=grid.SelectedRows.Count>0?grid.SelectedRows[0].Index:0;loading=true;name.Text=preset.Name;enabled.Checked=preset.Enabled;preamp.Value=(decimal)preset.Preamp;grid.Rows.Clear();for(int i=0;i<preset.Bands.Count;i++){var b=preset.Bands[i];grid.Rows.Add(i+1,Preset.Labels[Array.IndexOf(Preset.Types,b.Type)],b.Frequency.ToString("F1"),b.Gain.ToString("+0.0;-0.0;0.0"),b.Q.ToString("0.###"),b.Channel=="both"?"双声道":b.Channel=="left"?"左":"右",b.Enabled?"启用":"旁路");}if(grid.Rows.Count>0){grid.ClearSelection();grid.Rows[Math.Min(selected,grid.Rows.Count-1)].Selected=true;}loading=false;LoadEditor();chart.Invalidate();}
 void LoadEditor(){if(loading||grid.SelectedRows.Count==0)return;int n=grid.SelectedRows[0].Index;if(n>=preset.Bands.Count)return;var b=preset.Bands[n];type.SelectedIndex=Array.IndexOf(Preset.Types,b.Type);channel.SelectedIndex=Array.IndexOf(new[]{"both","left","right"},b.Channel);frequency.Value=(decimal)b.Frequency;gain.Value=(decimal)b.Gain;q.Value=(decimal)b.Q;bandEnabled.Checked=b.Enabled;}
 void ApplyBand(){if(grid.SelectedRows.Count==0)return;Remember();var b=preset.Bands[grid.SelectedRows[0].Index];b.Type=Preset.Types[Math.Max(0,type.SelectedIndex)];b.Frequency=(double)frequency.Value;b.Gain=(double)gain.Value;b.Q=(double)q.Value;b.Channel=new[]{"both","left","right"}[Math.Max(0,channel.SelectedIndex)];b.Enabled=bandEnabled.Checked;Changed();Reload();}
 void AddBand(){if(preset.Bands.Count>=32){MessageBox.Show("最多 32 段");return;}Remember();preset.Bands.Add(new Band());Changed();Reload();grid.ClearSelection();grid.Rows[^1].Selected=true;grid.FirstDisplayedScrollingRowIndex=grid.Rows.Count-1;}
 void DeleteBand(){if(grid.SelectedRows.Count==0)return;Remember();preset.Bands.RemoveAt(grid.SelectedRows[0].Index);Changed();Reload();}
 void Undo(){if(history.Count==0)return;preset=Preset.Parse(history[^1]);history.RemoveAt(history.Count-1);Changed();Reload();}
 void Import(){using var d=new OpenFileDialog{Filter="OpenEQ JSON|*.json|所有文件|*.*"};if(d.ShowDialog()!=DialogResult.OK)return;try{var next=Preset.Parse(File.ReadAllText(d.FileName));Remember();preset=next;Changed();Reload();}catch(Exception e){Error(e);}}
 void Export(){using var d=new SaveFileDialog{Filter="OpenEQ JSON|*.json",FileName="OpenEQ-preset.json"};if(d.ShowDialog()==DialogResult.OK)try{File.WriteAllText(d.FileName,preset.Encode());}catch(Exception e){Error(e);}}
 void Rename(){using var d=new Form{Text="预设名称",ClientSize=new Size(400,110),StartPosition=FormStartPosition.CenterParent};var t=new TextBox{Text=preset.Name,MaxLength=100,Dock=DockStyle.Top};d.Controls.Add(t);var save=Button("保存",()=>{Remember();preset.Name=t.Text;Changed();d.Close();});save.Dock=DockStyle.Bottom;d.Controls.Add(save);d.ShowDialog(this);}
 void LocalPresets(){var dir=Path.Combine(Path.GetDirectoryName(saved)!,"presets");Directory.CreateDirectory(dir);var menu=new ContextMenuStrip();menu.Items.Add("保存当前预设",null,(s,e)=>{var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(preset.Name)));File.WriteAllText(Path.Combine(dir,hash+".json"),preset.Encode());});menu.Items.Add(new ToolStripSeparator());foreach(var file in Directory.EnumerateFiles(dir,"*.json").Take(100)){try{var p=Preset.Parse(File.ReadAllText(file));menu.Items.Add(p.Name,null,(s,e)=>{Remember();preset=p.Copy();Changed();Reload();});}catch{}}menu.Show(Cursor.Position);}
 void Compare(){var menu=new ContextMenuStrip();menu.Items.Add("当前存为 A",null,(s,e)=>compareA=preset.Encode());menu.Items.Add("当前存为 B",null,(s,e)=>compareB=preset.Encode());menu.Items.Add("切换到 A",null,(s,e)=>LoadSlot(compareA));menu.Items.Add("切换到 B",null,(s,e)=>LoadSlot(compareB));menu.Show(Cursor.Position);}
 void LoadSlot(string? data){if(data==null){MessageBox.Show("请先保存该对比槽");return;}Remember();preset=Preset.Parse(data);Changed();Reload();}
 void Advanced(){using var d=new Form{Text="输出与声道 · 步进 0.1",ClientSize=new Size(480,480),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=bg};var c=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(20)};d.Controls.Add(c);
  NumericUpDown Add(string title,decimal low,decimal high,double value){var n=Number(low,high,(decimal)value,1);c.Controls.Add(new Label{Text=title,AutoSize=true,Padding=new Padding(0,6,0,0)});c.Controls.Add(n);return n;}
  var g=Add("输出增益 dB",-60,0,preset.Output.Gain);var b=Add("平衡 −1 左 / +1 右",-1,1,preset.Output.Balance);var l=Add("左延时 ms",0,100,preset.Output.LeftDelay);var r=Add("右延时 ms",0,100,preset.Output.RightDelay);var ceiling=Add("峰值上限 dBFS",-12,0,preset.Output.Ceiling);
  var il=new CheckBox{Text="左反相",Checked=preset.Output.InvertLeft};var ir=new CheckBox{Text="右反相",Checked=preset.Output.InvertRight};var protect=new CheckBox{Text="联动峰值保护",Checked=preset.Output.Protection,AutoSize=true};c.Controls.Add(il);c.Controls.Add(ir);c.Controls.Add(protect);c.Controls.Add(new Label{Text="sample-peak，非 true-peak",AutoSize=true});
  c.Controls.Add(Button("保存",()=>{Remember();var o=preset.Output;o.Gain=(double)g.Value;o.Balance=(double)b.Value;o.LeftDelay=(double)l.Value;o.RightDelay=(double)r.Value;o.Ceiling=(double)ceiling.Value;o.InvertLeft=il.Checked;o.InvertRight=ir.Checked;o.Protection=protect.Checked;Changed();d.Close();}));c.Controls.Add(Button("取消",()=>d.Close()));d.ShowDialog(this);
 }
 void Error(Exception e){state.Text="未启动 / 错误："+e.Message;MessageBox.Show(this,e.Message,"OpenEQ",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 sealed class Chart:Control {
  readonly Func<Preset> preset;public Chart(Func<Preset> p){preset=p;DoubleBuffered=true;BackColor=Color.White;}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float X(double f)=>40+(Width-65)*(float)(Math.Log10(f/20)/3);float Y(double db)=>12+(Height-38)*(float)((18-db)/60);
   using var line=new Pen(Color.FromArgb(228,233,226));using var font=new Font("Microsoft YaHei UI",8);foreach(int db in new[]{12,0,-12,-24,-36}){e.Graphics.DrawLine(line,40,Y(db),Width-20,Y(db));e.Graphics.DrawString(db.ToString(),font,Brushes.Gray,3,Y(db)-7);}foreach(int hz in new[]{20,100,1000,10000,20000}){e.Graphics.DrawLine(line,X(hz),12,X(hz),Height-26);e.Graphics.DrawString(hz>=1000?hz/1000+"k":hz.ToString(),font,Brushes.Gray,X(hz)-8,Height-23);}
   foreach(bool left in new[]{false,true}){var pts=Enumerable.Range(0,301).Select(n=>{double f=20*Math.Pow(1000,n/300.0);return new PointF(X(f),Y(Math.Clamp(preset().Response(f,left),-42,18)));}).ToArray();using var pen=new Pen(left?Color.FromArgb(21,112,87):Color.FromArgb(148,163,154),left?2:3);e.Graphics.DrawLines(pen,pts);}
  }
 }
}
