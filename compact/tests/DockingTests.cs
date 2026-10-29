using CodexEdgeWidget;

internal static class DockingTests {
 public static void Run(Action<string,Action> check) {
  void Eq<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}, got {actual}"); }
  void Inside(DockBounds work,DockBounds value) {
   if(value.X<work.X||value.Y<work.Y||(long)value.X+value.Width>(long)work.X+work.Width||(long)value.Y+value.Height>(long)work.Y+work.Height)throw new Exception("Dock extends beyond monitor work area");
  }
  var work=new DockBounds(-1920,-100,1920,1040);
  check("four edges anchor collapsed panel at normalized position",()=>{
   Eq(new DockBounds(-1920,139,26,84),DockGeometry.Calculate(work,1,new(DockEdge.Left,.25),false).Bounds);
   Eq(new DockBounds(-26,139,26,84),DockGeometry.Calculate(work,1,new(DockEdge.Right,.25),false).Bounds);
   Eq(new DockBounds(-1461,-100,84,26),DockGeometry.Calculate(work,1,new(DockEdge.Top,.25),false).Bounds);
   Eq(new DockBounds(-1461,914,84,26),DockGeometry.Calculate(work,1,new(DockEdge.Bottom,.25),false).Bounds);
  });
  check("left and right hover expansion preserves edge gauge anchor",()=>{
   foreach(var edge in new[]{DockEdge.Left,DockEdge.Right}) {
    var small=DockGeometry.Calculate(work,1.5,new(edge,.8),false);var large=DockGeometry.Calculate(work,1.5,new(edge,.8),true);
    Eq(39,small.Bounds.Width);Eq(276,large.Bounds.Width);Eq(126,large.Bounds.Height);Eq(small.Bounds.Y,large.Bounds.Y);
    Eq(edge==DockEdge.Left,large.DetailsOnRight);
    if(edge==DockEdge.Left)Eq(small.Bounds.X,large.Bounds.X);else Eq(small.Bounds.X+small.Bounds.Width,large.Bounds.X+large.Bounds.Width);
   }
  });
  check("top and bottom hover expansion follows gauge and fits inward",()=>{
   foreach(var edge in new[]{DockEdge.Top,DockEdge.Bottom})foreach(var fraction in new[]{0.0,.2,.8,1.0}) {
    var small=DockGeometry.Calculate(work,1,new(edge,fraction),false);var large=DockGeometry.Calculate(work,1,new(edge,fraction),true);
    Eq(84,small.Bounds.Width);Eq(26,small.Bounds.Height);Eq(184,large.Bounds.Width);Eq(110,large.Bounds.Height);Inside(work,large.Bounds);
    Eq(fraction<.5,large.DetailsOnRight);
    if(edge==DockEdge.Top)Eq(small.Bounds.Y,large.Bounds.Y);else Eq(small.Bounds.Y+small.Bounds.Height,large.Bounds.Y+large.Bounds.Height);
    Eq(small.Bounds.X,large.Bounds.X+large.GaugeOffsetX);
    if(large.GaugeOffsetX<0||large.GaugeOffsetX+small.Bounds.Width>large.Bounds.Width)throw new Exception("Horizontal gauge band is outside expanded panel");
   }
  });
  check("horizontal hover band preserves anchor with negative coordinates and scaled displays",()=>{
   foreach(var scale in new[]{.75,1.25,1.75,2.0})foreach(var edge in new[]{DockEdge.Top,DockEdge.Bottom})foreach(var fraction in new[]{0.0,.35,.9,1.0}) {
    var small=DockGeometry.Calculate(work,scale,new(edge,fraction),false);var large=DockGeometry.Calculate(work,scale,new(edge,fraction),true);
    Eq((int)Math.Round(84*scale),small.Bounds.Width);Eq((int)Math.Round(26*scale),small.Bounds.Height);
    Eq((int)Math.Round(184*scale),large.Bounds.Width);Eq((int)Math.Round(110*scale),large.Bounds.Height);
    Eq(small.Bounds.X,large.Bounds.X+large.GaugeOffsetX);Inside(work,large.Bounds);
    if(edge==DockEdge.Top)Eq(work.Y,large.Bounds.Y);else Eq(work.Y+work.Height,large.Bounds.Y+large.Bounds.Height);
   }
  });
  check("dock geometry clamps malformed fraction and handles missing scale",()=>{
   Eq(-100,DockGeometry.Calculate(work,double.NaN,new(DockEdge.Right,-20),false).Bounds.Y);
   Eq(856,DockGeometry.Calculate(work,0,new(DockEdge.Left,20),false).Bounds.Y);
   Eq(378,DockGeometry.Calculate(work,double.PositiveInfinity,new(DockEdge.Right,double.NaN),false).Bounds.Y);
  });
  check("tiny monitor never strands expanded panel outside work area",()=>{
   var tiny=new DockBounds(-10,2,12,22);
   foreach(var edge in Enum.GetValues<DockEdge>())foreach(var expanded in new[]{false,true})Inside(tiny,DockGeometry.Calculate(tiny,2,new(edge,.9),expanded).Bounds);
  });
  check("snap selects all four nearest edges and retains monitor identity",()=>{
   var positions=new[]{(X:-1918,Y:200,Edge:DockEdge.Left),(X:-30,Y:200,Edge:DockEdge.Right),(X:-900,Y:-97,Edge:DockEdge.Top),(X:-900,Y:852,Edge:DockEdge.Bottom)};
   foreach(var item in positions) {
    var dock=DockGeometry.Snap(work,1,item.X,item.Y,"DISPLAY2");Eq(item.Edge,dock.Edge);Eq("DISPLAY2",dock.Monitor);
    var bounds=DockGeometry.Calculate(work,1,dock,false).Bounds;Inside(work,bounds);
   }
  });
  check("snap round trips negative coordinates and DPI along edge",()=>{
   foreach(var edge in Enum.GetValues<DockEdge>())foreach(var fraction in new[]{.1,.35,.9}) {
    var desired=new DockPosition(edge,fraction,"DISPLAY2");var before=DockGeometry.Calculate(work,1.75,desired,false).Bounds;
    var restored=DockGeometry.Snap(work,1.75,before.X,before.Y,"DISPLAY2",DockGeometry.IsHorizontal(desired));Eq(edge,restored.Edge);Eq(before,DockGeometry.Calculate(work,1.75,restored,false).Bounds);
   }
  });
  check("snap rotates around the moving panel center along the new edge",()=>{
   var top=DockGeometry.Snap(work,1,-900,-99,"DISPLAY2");Eq(DockEdge.Top,top.Edge);
   var horizontal=DockGeometry.Calculate(work,1,top,false).Bounds;Eq(-887.0,horizontal.X+horizontal.Width/2.0);
   var left=DockGeometry.Snap(work,1,-1918,200,"DISPLAY2",true);Eq(DockEdge.Left,left.Edge);
   var vertical=DockGeometry.Calculate(work,1,left,false).Bounds;Eq(213.0,vertical.Y+vertical.Height/2.0);
  });
  check("horizontal snap uses the moving short height when selecting bottom edge",()=>{
   var position=DockGeometry.Snap(work,1,-900,912,"DISPLAY2",true);Eq(DockEdge.Bottom,position.Edge);
   Eq(new DockBounds(-900,914,84,26),DockGeometry.Calculate(work,1,position,false).Bounds);
  });
  var directory=Path.Combine(Path.GetTempPath(),"CodexDockTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  try {
   check("legacy style settings load default unlocked right-center dock",()=>{
    var path=Path.Combine(directory,"legacy.json");File.WriteAllText(path,"{\"style\":\"vertical\"}");var store=new PreferenceStore(path);
    Eq(DisplayStyle.Vertical,store.Load());Eq(new DockPreferences(new(DockEdge.Right,.5),false),store.LoadDock());
   });
   check("dock position and lock survive a new process store",()=>{
    var path=Path.Combine(directory,"restore.json");var expected=new DockPreferences(new(DockEdge.Bottom,.73,"\\\\.\\DISPLAY2"),true);
    Eq(true,new PreferenceStore(path).SaveDock(expected));Eq(expected,new PreferenceStore(path).LoadDock());
   });
   check("changing style preserves location and lock",()=>{
    var path=Path.Combine(directory,"style-preserves.json");var expected=new DockPreferences(new(DockEdge.Left,.39,"DISPLAY1"),true);var store=new PreferenceStore(path);
    store.SaveDock(expected);foreach(var style in Enum.GetValues<DisplayStyle>()) {Eq(true,store.Save(style));Eq(style,new PreferenceStore(path).Load());Eq(expected,new PreferenceStore(path).LoadDock());}
   });
   check("dragging and locking preserve last selected style",()=>{
    var path=Path.Combine(directory,"dock-preserves.json");var store=new PreferenceStore(path);store.Save(DisplayStyle.Vertical);
    foreach(var locked in new[]{false,true,false}) {var dock=new DockPreferences(new(DockEdge.Top,.25,"DISPLAY1"),locked);Eq(true,store.SaveDock(dock));Eq(DisplayStyle.Vertical,new PreferenceStore(path).Load());Eq(dock,new PreferenceStore(path).LoadDock());}
   });
   check("invalid dock data falls back without losing valid style",()=>{
    var path=Path.Combine(directory,"invalid.json");var store=new PreferenceStore(path);
    foreach(var dock in new[]{"null","[]","{}","{\"edge\":\"diagonal\",\"fraction\":0.2,\"locked\":true}","{\"edge\":\"left\",\"fraction\":\"bad\",\"locked\":true}","{\"edge\":\"left\",\"fraction\":0.2,\"locked\":\"yes\"}"}) {
     File.WriteAllText(path,"{\"style\":\"line\",\"dock\":"+dock+"}");Eq(DisplayStyle.Line,store.Load());Eq(new DockPreferences(new(DockEdge.Right,.5),false),store.LoadDock());
    }
   });
   check("dock save sanitizes invalid values and leaves no temporary files",()=>{
    var path=Path.Combine(directory,"sanitize.json");var store=new PreferenceStore(path);
    Eq(true,store.SaveDock(new(new((DockEdge)999,double.NaN,"DISPLAY1"),true)));Eq(new DockPreferences(new(DockEdge.Right,.5,"DISPLAY1"),true),store.LoadDock());
    Eq(true,store.SaveDock(new(new(DockEdge.Top,3),false)));Eq(1.0,store.LoadDock().Position.Fraction);Eq(0,Directory.GetFiles(directory,"*.tmp").Length);
   });
  } finally {Directory.Delete(directory,true);}
 }
}
