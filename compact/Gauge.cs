using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
namespace CodexEdgeWidget;
public sealed class Gauge(Color color) : FrameworkElement {
 public double? Fraction {get;set;}
 public DisplayStyle DisplayStyle {get;set;}
 protected override void OnRender(DrawingContext dc) {
  base.OnRender(dc);
  var track=new SolidColorBrush(Color.FromRgb(65,70,77));var fill=new SolidColorBrush(color);
  var trackPen=new Pen(track,2.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
  var pen=new Pen(fill,2.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
  double value=Fraction is {} f&&double.IsFinite(f)?Math.Clamp(f,0,1):0;
  if(DisplayStyle==DisplayStyle.Line) {
   dc.DrawRoundedRectangle(track,null,new Rect(1,7.5,16,3),1.5,1.5);
   if(value>0)dc.DrawRoundedRectangle(fill,null,new Rect(1,7.5,16*value,3),1.5,1.5);
  } else {
   var c=new Point(9,9); const double r=7;
   dc.DrawEllipse(null,trackPen,c,r,r);
   if(value>=1)dc.DrawEllipse(null,pen,c,r,r);
   else if(value>0) {
    double a=2*Math.PI*value-Math.PI/2;
    var geo=new StreamGeometry();using(var ctx=geo.Open()) {
     ctx.BeginFigure(new Point(9,2),false,false);
     ctx.ArcTo(new Point(9+r*Math.Cos(a),9+r*Math.Sin(a)),new Size(r,r),0,value>.5,SweepDirection.Clockwise,true,false);
    }
    geo.Freeze();dc.DrawGeometry(null,pen,geo);
   }
  }
 }
}
