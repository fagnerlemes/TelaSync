using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Tela {
 public sealed class DisplayRecord {
  public string Key, Model, DeviceName, PathData, SourceData, TargetData;
  public string Alias;
  public int Number, X, Y, Width, Height;
  public bool IsPrimary, InitialPosition;
  public DisplayRecord Copy() { return Json.Clone(this); }
  public int Right { get { return checked(X+Width); } }
  public int Bottom { get { return checked(Y+Height); } }
  public string Name { get { return String.IsNullOrWhiteSpace(Alias) ? Model : Alias; } }
 }
 public sealed class NamedProfile {
  public string Name, PrimaryKey;
  public List<DisplayRecord> Displays = new List<DisplayRecord>();
  public List<string> Enabled = new List<string>();
 }
 public sealed class AppState {
  public int Version=1;
  public List<DisplayRecord> Remembered=new List<DisplayRecord>();
  public List<NamedProfile> Profiles=new List<NamedProfile>();
  public List<string> PowerRequestedOff=new List<string>();
  public string PrimaryKey, LastAppliedFingerprint;
  public bool PreservePositions=true, DarkTheme=true;
  public string TvKey;
  public NamedProfile BeforeTv;
 }
 public static class Json {
  static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=4*1024*1024, RecursionLimit=60 }; }
  public static string Encode(object value) { return Serializer().Serialize(value); }
  public static T Decode<T>(string value) { return Serializer().Deserialize<T>(value); }
  public static T Clone<T>(T value) { return Decode<T>(Encode(value)); }
  public static void Write(string path, object value) {
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {
    using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
     byte[] bytes=new UTF8Encoding(false).GetBytes(Encode(value));stream.Write(bytes,0,bytes.Length);stream.Flush(true);
    }
    if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
   } finally { if(File.Exists(temp))File.Delete(temp); }
  }
  public static T Read<T>(string path) { return Decode<T>(File.ReadAllText(path,Encoding.UTF8)); }
 }
 public sealed class StateStore {
  public readonly string DirectoryPath;
  public string StatePath { get { return Path.Combine(DirectoryPath,"state.json"); } }
  public StateStore(string directory) { DirectoryPath=Path.GetFullPath(directory); }
  public AppState Load() {
   if(!File.Exists(StatePath))return new AppState();
   AppState result=Json.Read<AppState>(StatePath);
   if(result==null||result.Version!=1||result.Remembered==null||result.Profiles==null||result.PowerRequestedOff==null)
    throw new InvalidDataException("Formato de memória incompatível. Preserve o arquivo state.json e consulte o diagnóstico.");
   if(result.Remembered.Select(d=>d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=result.Remembered.Count)
    throw new InvalidDataException("A memória contém identidades de tela repetidas.");
   return result;
  }
  public void Save(AppState state) { Json.Write(StatePath,state); }
  public void Log(string message) {
   try { Directory.CreateDirectory(DirectoryPath);File.AppendAllText(Path.Combine(DirectoryPath,"diagnostico.log"),DateTimeOffset.Now.ToString("o")+" "+message+Environment.NewLine,Encoding.UTF8); } catch(IOException){} catch(UnauthorizedAccessException){}
  }
 }
 public static class MemoryLayout {
  public static void Merge(AppState state, List<DisplayRecord> active, bool updatePositions) {
   foreach(var live in active) {
    var saved=state.Remembered.FirstOrDefault(d=>Native.Same(d.Key,live.Key));
    if(saved==null) {
     var copy=live.Copy();copy.Number=state.Remembered.Count==0?1:state.Remembered.Max(d=>d.Number)+1;
     state.Remembered.Add(copy);
    } else if(updatePositions) {
     string alias=saved.Alias;int number=saved.Number;
     int index=state.Remembered.IndexOf(saved);state.Remembered[index]=live.Copy();state.Remembered[index].Alias=alias;state.Remembered[index].Number=number;
    } else {
     saved.DeviceName=live.DeviceName;saved.Model=live.Model;
    }
   }
   if(state.PrimaryKey==null||updatePositions)state.PrimaryKey=active.FirstOrDefault(d=>d.IsPrimary)!=null?active.First(d=>d.IsPrimary).Key:active.First().Key;
  }
 }
 public static class LayoutPlanner {
  public static bool Overlap(DisplayRecord a,DisplayRecord b) { return Math.Max(a.X,b.X)<Math.Min(a.Right,b.Right)&&Math.Max(a.Y,b.Y)<Math.Min(a.Bottom,b.Bottom); }
  public static bool Touch(DisplayRecord a,DisplayRecord b) {
   return ((a.Right==b.X||b.Right==a.X)&&Math.Max(a.Y,b.Y)<Math.Min(a.Bottom,b.Bottom)) ||
          ((a.Bottom==b.Y||b.Bottom==a.Y)&&Math.Max(a.X,b.X)<Math.Min(a.Right,b.Right));
  }
  static int Clamp(int v,int min,int max) { return Math.Max(min,Math.Min(v,max)); }
  public static List<DisplayRecord> Plan(IEnumerable<DisplayRecord> remembered,IEnumerable<string> enabled,string primary) {
   var keys=new HashSet<string>(enabled,StringComparer.OrdinalIgnoreCase);
   if(keys.Count==0)throw new InvalidOperationException("Mantenha pelo menos uma tela ativa.");
   var displays=remembered.Where(d=>keys.Contains(d.Key)).Select(d=>d.Copy()).ToList();
   if(displays.Count!=keys.Count)throw new InvalidOperationException("Há uma tela sem configuração salva neste perfil.");
   if(displays.Select(d=>d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=displays.Count)throw new InvalidOperationException("Identidade de tela ambígua.");
   if(displays.Any(d=>d.Width<=0||d.Height<=0||d.Width>32768||d.Height>32768))throw new InvalidOperationException("Dimensões de tela inválidas.");
   var anchor=displays.FirstOrDefault(d=>Native.Same(d.Key,primary))??displays.FirstOrDefault(d=>d.IsPrimary)??displays.OrderByDescending(d=>(long)d.Width*d.Height).First();
   int dx=anchor.X,dy=anchor.Y;
   foreach(var d in displays) { d.X=checked(d.X-dx);d.Y=checked(d.Y-dy);d.IsPrimary=d==anchor; }
   var placed=new List<DisplayRecord>{anchor};var remaining=displays.Where(d=>d!=anchor).ToList();
   while(remaining.Count>0) {
    var connected=remaining.FirstOrDefault(d=>placed.Any(p=>Touch(d,p))&&!placed.Any(p=>Overlap(d,p)));
    if(connected!=null) { placed.Add(connected);remaining.Remove(connected);continue; }
    DisplayRecord best=null,bestOriginal=null;long cost=Int64.MaxValue;
    foreach(var d in remaining)foreach(var p in placed) {
     var candidates=new[]{new[]{p.X-d.Width,Clamp(d.Y,p.Y-d.Height+1,p.Bottom-1)},new[]{p.Right,Clamp(d.Y,p.Y-d.Height+1,p.Bottom-1)},new[]{Clamp(d.X,p.X-d.Width+1,p.Right-1),p.Y-d.Height},new[]{Clamp(d.X,p.X-d.Width+1,p.Right-1),p.Bottom}};
     foreach(var pair in candidates) {
      var candidate=d.Copy();candidate.X=pair[0];candidate.Y=pair[1];
      long distance=Math.Abs((long)candidate.X-d.X)+Math.Abs((long)candidate.Y-d.Y);
      if(distance<cost&&!placed.Any(p2=>Overlap(candidate,p2))) { best=candidate;bestOriginal=d;cost=distance; }
     }
    }
    if(best==null)throw new InvalidOperationException("Não foi possível organizar as telas sem sobreposição.");
    bestOriginal.X=best.X;bestOriginal.Y=best.Y;placed.Add(bestOriginal);remaining.Remove(bestOriginal);
   }
   // Compaction is temporary. The remembered records were cloned and remain unchanged.
   return displays;
  }
  public static string Fingerprint(IEnumerable<DisplayRecord> displays) {
   return String.Join("|",displays.OrderBy(d=>d.Key,StringComparer.OrdinalIgnoreCase).Select(d=>d.Key.ToUpperInvariant()+":"+d.X+","+d.Y+","+d.Width+","+d.Height+","+Rotation(d)));
  }
  public static uint Rotation(DisplayRecord d) { return String.IsNullOrEmpty(d.PathData)?1:Native.U32(Convert.FromBase64String(d.PathData),40); }
 }
}
