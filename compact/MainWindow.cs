using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace CodexEdgeWidget;
public sealed class MainWindow : Window {
 private readonly IQuotaClient client;private readonly PreferenceStore preferences;private DisplayStyle style;
 private readonly CancellationTokenSource lifetime=new();private readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
 private readonly DispatcherTimer collapse=new(){Interval=TimeSpan.FromMilliseconds(240)};
 private readonly NativeDock dock;private readonly Grid details=new();private readonly Gauge[] gauges;private readonly VerticalBars verticalBars;
 private readonly TextBlock[] values=new TextBlock[3];private QuotaData? data;private Task? refresh;private DateTimeOffset nextRefresh;
 private bool shuttingDown,allowClose,pointerDown,positionLocked;private readonly bool demo;
 private readonly Grid layout,column;private readonly Border border;private readonly MenuItem lockItem;
 public MainWindow(IQuotaClient client,PreferenceStore preferences,bool demo=false,bool preview=false) {
  this.client=client;this.preferences=preferences;this.demo=demo;style=preferences.Load();
  Title="Codex 用量悬浮窗";Width=26;Height=84;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
  AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=demo||preview;ShowActivated=false;
  WindowStartupLocation=WindowStartupLocation.Manual;Left=SystemParameters.WorkArea.Right-26;Top=SystemParameters.WorkArea.Top+(SystemParameters.WorkArea.Height-84)/2;
  var background=new LinearGradientBrush(Color.FromRgb(43,47,53),Color.FromRgb(29,32,36),90);
  border=new Border{Background=background,BorderBrush=new SolidColorBrush(Color.FromRgb(77,83,91)),BorderThickness=new Thickness(1,1,0,1),CornerRadius=new CornerRadius(13,0,0,13),ClipToBounds=true};
  layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(25)});
  layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=new GridLength(0)});
  details.Margin=new Thickness(10,5,7,5);layout.Children.Add(details);
  column=new Grid{Margin=new Thickness(0,5,0,5)};Grid.SetColumn(column,1);layout.Children.Add(column);
  string[] labels=["5 小时剩余","距重置","本周剩余"];
  Color[] colors=[Color.FromRgb(96,214,188),Color.FromRgb(229,181,102),Color.FromRgb(117,169,237)];
  gauges=new Gauge[3];
  for(int i=0;i<3;i++) {
   details.RowDefinitions.Add(new(){Height=new GridLength(24)});column.RowDefinitions.Add(new(){Height=new GridLength(24)});
   var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(row,i);details.Children.Add(row);
   var label=new TextBlock{Text=labels[i],FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(210,214,219)),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(label);
   var value=new TextBlock{FontSize=11.5,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Text="—"};Grid.SetColumn(value,1);row.Children.Add(value);values[i]=value;
   if(i<2) {var separator=new Border{Height=.5,Background=new SolidColorBrush(Color.FromRgb(70,75,82)),VerticalAlignment=VerticalAlignment.Bottom};row.Children.Add(separator);Grid.SetColumnSpan(separator,2);}
   var gauge=new Gauge(colors[i]){Width=18,Height=18,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(gauge,i);column.Children.Add(gauge);gauges[i]=gauge;
  }
  verticalBars=new VerticalBars(colors){Width=18,Height=66,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
  Grid.SetRowSpan(verticalBars,3);column.Children.Add(verticalBars);
  border.Child=layout;Content=border;details.Visibility=Visibility.Collapsed;dock=new NativeDock(this,demo||preview);
  var savedDock=preferences.LoadDock();dock.Position=savedDock.Position;positionLocked=savedDock.Locked;
  dock.LayoutChanged+=UpdateDockLayout;dock.PositionChanged+=_=>SaveDock();
  dock.DragStarted+=()=>{collapse.Stop();details.Visibility=Visibility.Collapsed;UpdateDockLayout();};
  SourceInitialized+=(_,_)=>dock.Attach();Loaded+=(_,_)=>{if(preview)SetExpanded(true);Render();StartRefresh();clock.Start();};
  MouseEnter+=(_,_)=>{collapse.Stop();if(!pointerDown)SetExpanded(true);};MouseLeave+=(_,_)=>{if(!pointerDown)collapse.Start();};
  collapse.Tick+=(_,_)=>{collapse.Stop();if(!pointerDown&&!IsMouseOver&&ContextMenu?.IsOpen!=true)SetExpanded(false);};
  MouseLeftButtonDown+=(_,e)=>{pointerDown=true;collapse.Stop();if(!positionLocked)dock.BeginPointer();CaptureMouse();e.Handled=true;};
  MouseMove+=(_,_)=>{if(pointerDown&&!positionLocked)dock.UpdatePointer();};
  MouseLeftButtonUp+=(_,e)=>{bool valid=pointerDown;bool dragged=dock.EndPointer();pointerDown=false;ReleaseMouseCapture();if(valid&&!dragged){style=QuotaDisplay.Toggle(style);preferences.Save(style);Render();}if(!IsMouseOver)collapse.Start();e.Handled=true;};
  LostMouseCapture+=(_,_)=>{if(pointerDown){pointerDown=false;dock.EndPointer();if(!IsMouseOver)collapse.Start();}};
  lockItem=new MenuItem{Header=positionLocked?"解除锁定位置":"锁定位置"};
  lockItem.Click+=(_,_)=>{positionLocked=!positionLocked;lockItem.Header=positionLocked?"解除锁定位置":"锁定位置";SaveDock();Render();};
  var exit=new MenuItem{Header="退出"};exit.Click+=(_,_)=>Close();ContextMenu=new ContextMenu();ContextMenu.Items.Add(lockItem);ContextMenu.Items.Add(new Separator());ContextMenu.Items.Add(exit);
  ContextMenu.Closed+=(_,_)=>{if(!IsMouseOver)collapse.Start();};
  clock.Tick+=(_,_)=>{Render();if(DateTimeOffset.UtcNow>=nextRefresh)StartRefresh();};Closing+=OnClosing;
 }
 private void SetExpanded(bool expanded) {dock.Expanded=expanded;details.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;dock.Place();}
 private void SaveDock()=>preferences.SaveDock(new(dock.Position,positionLocked));
 private void UpdateDockLayout() {
  bool right=dock.DetailsOnRight,horizontal=DockGeometry.IsHorizontal(dock.Position),top=dock.Position.Edge==DockEdge.Top;
  Width=dock.Expanded?184:horizontal?84:26;Height=horizontal?(dock.Expanded?110:26):84;
  if(horizontal) {
   layout.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);layout.ColumnDefinitions[1].Width=new GridLength(0);
   var detailHeight=dock.Expanded?new GridLength(1,GridUnitType.Star):new GridLength(0);
   layout.RowDefinitions[0].Height=top?new GridLength(26):detailHeight;layout.RowDefinitions[1].Height=top?detailHeight:new GridLength(26);
   Grid.SetColumn(column,0);Grid.SetColumn(details,0);Grid.SetRow(column,top?0:1);Grid.SetRow(details,top?1:0);
   column.HorizontalAlignment=HorizontalAlignment.Left;column.Width=74;column.Height=24;column.VerticalAlignment=VerticalAlignment.Center;
   column.Margin=new Thickness(dock.GaugeOffset+5,0,0,0);details.Margin=new Thickness(10,5,10,5);
  } else {
   layout.ColumnDefinitions[0].Width=right?new GridLength(25):new GridLength(1,GridUnitType.Star);layout.ColumnDefinitions[1].Width=right?new GridLength(1,GridUnitType.Star):new GridLength(25);
   layout.RowDefinitions[0].Height=new GridLength(1,GridUnitType.Star);layout.RowDefinitions[1].Height=new GridLength(0);
   Grid.SetColumn(column,right?0:1);Grid.SetColumn(details,right?1:0);Grid.SetRow(column,0);Grid.SetRow(details,0);
   column.HorizontalAlignment=HorizontalAlignment.Stretch;column.VerticalAlignment=VerticalAlignment.Stretch;column.Width=double.NaN;column.Height=double.NaN;
   column.Margin=new Thickness(0,5,0,5);details.Margin=right?new Thickness(7,5,10,5):new Thickness(10,5,7,5);
  }
  column.RowDefinitions.Clear();column.ColumnDefinitions.Clear();
  for(int i=0;i<3;i++) {
   if(horizontal)column.ColumnDefinitions.Add(new(){Width=new GridLength(24)});else column.RowDefinitions.Add(new(){Height=new GridLength(24)});
   Grid.SetRow(gauges[i],horizontal?0:i);Grid.SetColumn(gauges[i],horizontal?i:0);
  }
  Grid.SetRowSpan(verticalBars,horizontal?1:3);Grid.SetColumnSpan(verticalBars,horizontal?3:1);
  verticalBars.Width=horizontal?66:18;verticalBars.Height=horizontal?18:66;verticalBars.Horizontal=horizontal;verticalBars.InvalidateVisual();
  border.CornerRadius=dock.Position.Edge switch {DockEdge.Left=>new(0,13,13,0),DockEdge.Top=>new(0,0,13,13),DockEdge.Bottom=>new(13,13,0,0),_=>new(13,0,0,13)};
  border.BorderThickness=dock.Position.Edge switch {DockEdge.Left=>new(0,1,1,1),DockEdge.Top=>new(1,0,1,1),DockEdge.Bottom=>new(1,1,1,0),_=>new(1,1,0,1)};
 }
 private void StartRefresh() {if(shuttingDown||refresh is {IsCompleted:false})return;nextRefresh=DateTimeOffset.UtcNow.AddSeconds(60);refresh=RefreshAsync();}
 private async Task RefreshAsync() {
  try {data=await client.FetchAsync(lifetime.Token);var reset=new[]{data.Primary,data.Secondary}.Where(p=>p?.WindowMinutes==300).Select(p=>p?.ResetsAt).FirstOrDefault();if(reset>DateTimeOffset.UtcNow&&reset<nextRefresh)nextRefresh=reset.Value.AddSeconds(1);}
  catch(OperationCanceledException) {if(!shuttingDown)data=null;}
  catch {data=null;}
  if(!shuttingDown)Render();
 }
 private void Render() {
  var view=QuotaDisplay.Calculate(data,DateTimeOffset.UtcNow);
  static string Percent(double? v)=>v is {} n?$"{n*100:0.#}%":"—";
  values[0].Text=Percent(view.FiveHourRemaining);values[1].Text=view.ResetText;values[2].Text=Percent(view.WeeklyRemaining);
  double?[] fractions=[view.FiveHourRemaining,view.ResetFraction,view.WeeklyRemaining];
  bool vertical=style==DisplayStyle.Vertical;
  for(int i=0;i<3;i++){gauges[i].Visibility=vertical?Visibility.Collapsed:Visibility.Visible;gauges[i].Fraction=fractions[i];gauges[i].DisplayStyle=style;gauges[i].InvalidateVisual();}
  verticalBars.Visibility=vertical?Visibility.Visible:Visibility.Collapsed;verticalBars.Fractions=fractions;verticalBars.InvalidateVisual();
  string mode=style switch {DisplayStyle.Line=>"横条",DisplayStyle.Vertical=>"竖条",_=>"圆环"};
  string name=$"5 小时剩余 {values[0].Text}，距重置 {values[1].Text}，本周剩余 {values[2].Text}，{mode}模式";
  AutomationProperties.SetName(this,name);AutomationProperties.SetHelpText(this,(demo?"演示数据。":"")+"青绿：5小时余额；琥珀：重置倒计时；蓝色：本周余额。单击切换样式，悬停看详情。"+(positionLocked?"位置已锁定，右键解除。":"拖动贴到屏幕四边，右键锁定位置。"));
 }
 private async void OnClosing(object? sender,CancelEventArgs e) {
  if(allowClose)return;e.Cancel=true;if(shuttingDown)return;shuttingDown=true;if(pointerDown){pointerDown=false;dock.EndPointer();ReleaseMouseCapture();}SaveDock();preferences.Save(style);clock.Stop();collapse.Stop();lifetime.Cancel();
  try {if(refresh!=null)await refresh;await client.DisposeAsync();}
  catch { /* A failed child-process cleanup must not crash the dispatcher on exit. */ }
  finally {lifetime.Dispose();allowClose=true;_ = Dispatcher.BeginInvoke(new Action(Close));}
 }
}
