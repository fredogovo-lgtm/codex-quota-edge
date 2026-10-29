using System.Windows;
using System.Windows.Media;
namespace CodexEdgeWidget;

// Reference style: three parallel bars share a baseline and fill from bottom to top.
public sealed class VerticalBars(Color[] colors) : FrameworkElement {
 public double?[] Fractions {get;set;}=[null,null,null];
 public bool Horizontal {get;set;}
 protected override void OnRender(DrawingContext dc) {
  base.OnRender(dc);
  var track=new SolidColorBrush(Color.FromRgb(65,70,77));
  double height=ActualHeight;
  for(int i=0;i<3;i++) {
   double x=1+i*6;
   dc.DrawRoundedRectangle(track,null,Horizontal?new Rect(0,x,ActualWidth,3):new Rect(x,0,3,height),1.5,1.5);
   double value=Fractions.Length>i&&Fractions[i] is {} f&&double.IsFinite(f)?Math.Clamp(f,0,1):0;
   if(value>0)dc.DrawRoundedRectangle(new SolidColorBrush(colors[i]),null,Horizontal?new Rect(0,x,ActualWidth*value,3):new Rect(x,height*(1-value),3,height*value),1.5,1.5);
  }
 }
}
