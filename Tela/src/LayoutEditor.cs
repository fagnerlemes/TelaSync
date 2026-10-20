using System;
using System.Collections.Generic;
using System.Linq;

namespace Tela {
 public sealed class LayoutEditor {
  public readonly List<DisplayRecord> Displays;
  public string PrimaryKey;
  public LayoutEditor(IEnumerable<DisplayRecord> displays,string primary){Displays=displays.Select(d=>d.Copy()).ToList();PrimaryKey=primary;}
  public DisplayRecord Find(string key){return Displays.First(d=>Native.Same(d.Key,key));}
  public void Move(string key,int x,int y){if(Math.Abs((long)x)>100000||Math.Abs((long)y)>100000)throw new InvalidOperationException("Use coordenadas entre -100000 e 100000.");var d=Find(key);d.X=x;d.Y=y;d.InitialPosition=false;}
  public void Number(string key,int value){if(value<1||value>99)throw new InvalidOperationException("O número da tela deve ficar entre 1 e 99.");var d=Find(key);var other=Displays.FirstOrDefault(r=>r!=d&&r.Number==value);if(other!=null)other.Number=d.Number;d.Number=value;}
  public void MakePrimary(string key){PrimaryKey=key;foreach(var d in Displays)d.IsPrimary=Native.Same(d.Key,key);}
  public void Rotate(string key,int direction){
   if(direction!=1&&direction!=-1)throw new ArgumentException("Escolha horário ou anti-horário.");
   var d=Find(key);var path=Convert.FromBase64String(d.PathData);uint current=Native.U32(path,40);
   if(current<1||current>4)throw new InvalidOperationException("A orientação atual desta tela não é válida.");
   Native.Put(path,40,(uint)(((int)current-1+direction+4)%4+1));d.PathData=Convert.ToBase64String(path);
   int width=d.Width;d.Width=d.Height;d.Height=width;
   // CCD source modes keep their unrotated dimensions. Only the target rotates.
   // Keep source/target timing data, identity and all other adapters unchanged.
  }
  public void Snap(string key,int tolerance){
   var d=Find(key);var others=Displays.Where(r=>r!=d&&!r.InitialPosition).ToList();
   var xs=others.SelectMany(r=>new[]{r.X,r.Right,r.X-d.Width,r.Right-d.Width}).OrderBy(x=>Math.Abs((long)x-d.X)).ToList();
   var ys=others.SelectMany(r=>new[]{r.Y,r.Bottom,r.Y-d.Height,r.Bottom-d.Height}).OrderBy(y=>Math.Abs((long)y-d.Y)).ToList();
   if(xs.Any()&&Math.Abs((long)xs[0]-d.X)<=tolerance)d.X=xs[0];if(ys.Any()&&Math.Abs((long)ys[0]-d.Y)<=tolerance)d.Y=ys[0];
   if(!others.Any(r=>LayoutPlanner.Overlap(d,r)))return;
   DisplayRecord best=null;long cost=Int64.MaxValue;
   foreach(var r in others)foreach(var p in new[]{new[]{r.X-d.Width,d.Y},new[]{r.Right,d.Y},new[]{d.X,r.Y-d.Height},new[]{d.X,r.Bottom}}){var candidate=d.Copy();candidate.X=p[0];candidate.Y=p[1];long distance=Math.Abs((long)d.X-p[0])+Math.Abs((long)d.Y-p[1]);if(distance<cost&&!others.Any(o=>LayoutPlanner.Overlap(candidate,o))){best=candidate;cost=distance;}}
   if(best!=null){d.X=best.X;d.Y=best.Y;}
  }
  public List<DisplayRecord> Normalize(){var anchor=Find(PrimaryKey);int x=anchor.X,y=anchor.Y;var copy=Displays.Select(d=>d.Copy()).ToList();foreach(var d in copy){d.X=checked(d.X-x);d.Y=checked(d.Y-y);d.IsPrimary=Native.Same(d.Key,PrimaryKey);}return copy;}
 }
}
