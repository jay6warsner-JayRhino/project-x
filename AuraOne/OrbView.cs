using Android.Content;
using Android.Graphics;
using Android.Views;
namespace AuraOne;
public sealed class OrbView(Context context):View(context)
{
 public bool Active,ShowFace,Speaking;public float Level;public string Label="";float phase;
 protected override void OnDraw(Canvas c)
 {
  base.OnDraw(c);phase+=(Active||Speaking) ? .028f : .009f;var x=Width/2f;var y=Height/2f;var r=Math.Min(Width,Height)*.43f;
  using var p=new Paint(PaintFlags.AntiAlias);p.SetStyle(Paint.Style.Fill);
  using(var glow=new RadialGradient(x,y,r*1.17f,new[]{Color.Argb(55,156,175,255),Color.Argb(0,255,255,255)}.Select(c=>c.ToArgb()).ToArray(),null,Shader.TileMode.Clamp!)){p.SetShader(glow);c.DrawCircle(x,y,r*1.17f,p);p.SetShader(null);}
  using(var fill=new LinearGradient(x-r,y-r,x+r,y+r,new[]{Color.Rgb(64,124,255),Color.Rgb(146,174,255),Color.Rgb(238,210,244),Color.Rgb(255,137,129)}.Select(c=>c.ToArgb()).ToArray(),new[]{0f,.4f,.7f,1f},Shader.TileMode.Clamp!)){p.SetShader(fill);c.DrawCircle(x,y,r,p);p.SetShader(null);}
  c.Save();using var circle=new Android.Graphics.Path();circle.AddCircle(x,y,r,Android.Graphics.Path.Direction.Cw!);c.ClipPath(circle);
  using var wave=new Android.Graphics.Path();var drift=(float)Math.Sin(phase)*r*.14f;wave.MoveTo(x-r,y+drift);wave.CubicTo(x-r*.55f,y+r*.35f,x-r*.4f,y-r*.52f,x,y-r*.18f);wave.CubicTo(x+r*.45f,y+r*.3f,x+r*.4f,y-r*.22f,x+r,y-r*.07f);wave.LineTo(x+r,y+r);wave.LineTo(x-r,y+r);wave.Close();
  using(var light=new LinearGradient(x-r,y,x+r,y+r,new[]{Color.Argb(190,224,236,255),Color.Argb(110,255,251,255),Color.Argb(55,255,192,187)}.Select(c=>c.ToArgb()).ToArray(),null,Shader.TileMode.Clamp!)){p.SetShader(light);c.DrawPath(wave,p);p.SetShader(null);}
  using var second=new Android.Graphics.Path();second.MoveTo(x-r,y+r*.5f);second.CubicTo(x-r*.2f,y-r*.1f,x-r*.15f,y+r*.66f,x+r*.32f,y+r*.12f);second.CubicTo(x+r*.7f,y-r*.22f,x+r,y+r*.02f,x+r,y+r*.18f);second.LineTo(x+r,y+r);second.LineTo(x-r,y+r);second.Close();
  using(var coral=new LinearGradient(x,y+r,x+r,y,new[]{Color.Argb(80,220,212,255),Color.Argb(150,255,115,112)}.Select(c=>c.ToArgb()).ToArray(),null,Shader.TileMode.Clamp!)){p.SetShader(coral);c.DrawPath(second,p);p.SetShader(null);}
  using(var gloss=new RadialGradient(x-r*.3f,y-r*.55f,r*.9f,new[]{Color.Argb(100,255,255,255),Color.Argb(0,255,255,255)}.Select(c=>c.ToArgb()).ToArray(),null,Shader.TileMode.Clamp!)){p.SetShader(gloss);c.DrawCircle(x,y,r,p);p.SetShader(null);}c.Restore();
  p.SetStyle(Paint.Style.Stroke);p.StrokeWidth=Math.Max(2,r*.012f);p.Color=Color.Argb(215,255,255,255);c.DrawCircle(x,y,r,p);
  p.Color=Color.Rgb(150,177,255);c.DrawArc(x-r-9,y-r-9,x+r+9,y+r+9,95+drift*.05f,172,false,p);p.Color=Color.Rgb(255,166,169);c.DrawArc(x-r-9,y-r-9,x+r+9,y+r+9,275+drift*.05f,145,false,p);
  if(ShowFace){p.SetStyle(Paint.Style.Fill);p.Color=Color.Argb(200,255,255,255);c.DrawCircle(x-r*.22f,y-r*.06f,r*.055f,p);c.DrawCircle(x+r*.22f,y-r*.06f,r*.055f,p);p.SetStyle(Paint.Style.Stroke);p.StrokeWidth=3;c.DrawArc(x-r*.22f,y+r*.08f,x+r*.22f,y+r*(Speaking?.36f:.22f),0,180,false,p);}
  if(IsAttachedToWindow&&IsShown)PostInvalidateDelayed(50);
 }
}

