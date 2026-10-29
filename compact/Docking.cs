namespace CodexEdgeWidget;
public enum DockEdge {Left,Top,Right,Bottom}
public record DockPosition(DockEdge Edge,double Fraction,string? Monitor=null);
public record DockPreferences(DockPosition Position,bool Locked);
public record DockPlacement(DockBounds Bounds,bool DetailsOnRight,int GaugeOffsetX=0);
public static class DockGeometry {
 public static DockPosition Normalize(DockPosition p)=>new(Enum.IsDefined(p.Edge)?p.Edge:DockEdge.Right,double.IsFinite(p.Fraction)?Math.Clamp(p.Fraction,0,1):.5,string.IsNullOrWhiteSpace(p.Monitor)?null:p.Monitor);
 public static bool IsHorizontal(DockPosition p)=>Normalize(p).Edge is DockEdge.Top or DockEdge.Bottom;
 private static (int Width,int Height) Size(DockBounds work,double scale,bool expanded,bool horizontal) {
  scale=double.IsFinite(scale)&&scale>0?Math.Clamp(scale,.25,16):1;
  int width=Math.Min(Math.Max(0,work.Width),(int)Math.Round((expanded?184:horizontal?84:26)*scale));
  int height=Math.Min(Math.Max(0,work.Height),(int)Math.Round((horizontal?(expanded?110:26):84)*scale));
  return (width,height);
 }
 public static DockPlacement Calculate(DockBounds work,double scale,DockPosition position,bool expanded) {
  var p=Normalize(position);bool horizontal=IsHorizontal(p);var (w,h)=Size(work,scale,expanded,horizontal);var (small,_)=Size(work,scale,false,horizontal);
  int right=work.X+Math.Max(0,work.Width),bottom=work.Y+Math.Max(0,work.Height);
  int anchor=work.X+(int)Math.Round(Math.Max(0,work.Width-small)*p.Fraction);
  int y=work.Y+(int)Math.Round(Math.Max(0,work.Height-h)*p.Fraction);
  bool detailsRight=p.Edge==DockEdge.Left||(p.Edge is DockEdge.Top or DockEdge.Bottom&&p.Fraction<.5);
  int x=p.Edge switch {DockEdge.Left=>work.X,DockEdge.Right=>right-w,_=>anchor+(int)Math.Round((small-w)/2.0)};
  if(p.Edge==DockEdge.Top)y=work.Y;else if(p.Edge==DockEdge.Bottom)y=bottom-h;
  x=Math.Clamp(x,work.X,Math.Max(work.X,right-w));y=Math.Clamp(y,work.Y,Math.Max(work.Y,bottom-h));
  return new(new(x,y,w,h),detailsRight,horizontal?anchor-x:0);
 }
 public static DockPosition Snap(DockBounds work,double scale,int collapsedX,int collapsedY,string? monitor=null,bool horizontal=false) {
  var (w,h)=Size(work,scale,false,horizontal);
  var candidates=new[]{(Edge:DockEdge.Left,Gap:Math.Abs((long)collapsedX-work.X)),(Edge:DockEdge.Right,Gap:Math.Abs((long)collapsedX+w-work.X-work.Width)),(Edge:DockEdge.Top,Gap:Math.Abs((long)collapsedY-work.Y)),(Edge:DockEdge.Bottom,Gap:Math.Abs((long)collapsedY+h-work.Y-work.Height))};
  var edge=candidates.OrderBy(c=>c.Gap).First().Edge;
  var (targetWidth,targetHeight)=Size(work,scale,false,edge is DockEdge.Top or DockEdge.Bottom);
  double fraction=edge is DockEdge.Left or DockEdge.Right
   ?(work.Height>targetHeight?(collapsedY+h/2.0-work.Y-targetHeight/2.0)/(work.Height-targetHeight):.5)
   :(work.Width>targetWidth?(collapsedX+w/2.0-work.X-targetWidth/2.0)/(work.Width-targetWidth):.5);
  return Normalize(new(edge,fraction,monitor));
 }
}
