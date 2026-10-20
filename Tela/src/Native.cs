using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Tela {
 public sealed class PathBinding {
  public string Key,Name,DeviceName;
  public byte[] Data;
  public bool Active,Available;
  public string SourceKey { get { return Convert.ToBase64String(Data.Take(12).ToArray()); } }
 }
 public sealed class NativeTopology {
  public List<byte[]> Paths=new List<byte[]>(),Modes=new List<byte[]>();
  public List<PathBinding> Bindings=new List<PathBinding>();
  public List<DisplayRecord> Active=new List<DisplayRecord>();
  public TopologyImage Image() { return new TopologyImage { Paths=Paths.Where(p=>(Native.U32(p,68)&1)!=0).Select(Convert.ToBase64String).ToList(),Modes=Modes.Select(Convert.ToBase64String).ToList() }; }
 }
 public sealed class TopologyImage {
  public List<string> Paths=new List<string>(),Modes=new List<string>();
 }
 public static class Native {
  // Windows SDK layouts, x64: PATH_INFO 72, MODE_INFO 64, DEVICE_INFO_HEADER 20.
  [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
  [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint paths,IntPtr pathArray,ref uint modes,IntPtr modeArray,IntPtr topology);
  [DllImport("user32.dll")]static extern int SetDisplayConfig(uint paths,IntPtr pathArray,uint modes,IntPtr modeArray,uint flags);
  [DllImport("user32.dll")]static extern int DisplayConfigGetDeviceInfo(IntPtr packet);
  [StructLayout(LayoutKind.Sequential)]public struct Rect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]public struct MonitorInfo { public uint Size;public Rect Bounds,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device; }
  public delegate bool MonitorCallback(IntPtr handle,IntPtr dc,IntPtr rect,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll")]public static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
  [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr window,int id);
  [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr window);
  public static bool Same(string a,string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
  public static uint U32(byte[] b,int offset) { return BitConverter.ToUInt32(b,offset); }
  public static int I32(byte[] b,int offset) { return BitConverter.ToInt32(b,offset); }
  public static void Put(byte[] b,int offset,uint value) { Array.Copy(BitConverter.GetBytes(value),0,b,offset,4); }
  public static void PutInt(byte[] b,int offset,int value) { Array.Copy(BitConverter.GetBytes(value),0,b,offset,4); }
  static string Wide(byte[] b,int offset,int bytes) { return Encoding.Unicode.GetString(b,offset,bytes).Split('\0')[0]; }
  static byte[] Slice(byte[] b,int offset,int count) { var result=new byte[count];Array.Copy(b,offset,result,0,count);return result; }
  static byte[] Request(uint kind,int size,byte[] adapter,uint id) {
   byte[] packet=new byte[size];Put(packet,0,kind);Put(packet,4,(uint)size);Array.Copy(adapter,0,packet,8,8);Put(packet,16,id);
   IntPtr memory=Marshal.AllocHGlobal(size);
   try { Marshal.Copy(packet,0,memory,size);int code=DisplayConfigGetDeviceInfo(memory);if(code!=0)throw new Win32Exception(code,"Não foi possível identificar a tela: "+new Win32Exception(code).Message);Marshal.Copy(memory,packet,0,size);return packet; }
   finally { Marshal.FreeHGlobal(memory); }
  }
  public static Dictionary<string,Tuple<IntPtr,MonitorInfo>> Monitors() {
   var result=new Dictionary<string,Tuple<IntPtr,MonitorInfo>>(StringComparer.OrdinalIgnoreCase);
   MonitorCallback callback=delegate(IntPtr handle,IntPtr dc,IntPtr r,IntPtr data){var info=new MonitorInfo{Size=(uint)Marshal.SizeOf(typeof(MonitorInfo))};if(GetMonitorInfoW(handle,ref info))result[info.Device]=Tuple.Create(handle,info);return true;};
   if(!EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero))throw new InvalidOperationException("Não foi possível enumerar as áreas do desktop.");
   GC.KeepAlive(callback);return result;
  }
  public static NativeTopology Capture(bool allPaths) {
   uint flags=allPaths?1u:2u;
   for(int attempt=0;attempt<5;attempt++) {
    uint pc,mc;int code=GetDisplayConfigBufferSizes(flags,out pc,out mc);if(code!=0)throw new Win32Exception(code);
    if(pc>4096||mc>16384)throw new InvalidOperationException("Inventário de telas excede o limite suportado.");
    IntPtr p=Marshal.AllocHGlobal(checked((int)Math.Max(1,pc)*72)),m=Marshal.AllocHGlobal(checked((int)Math.Max(1,mc)*64));
    try {
     code=QueryDisplayConfig(flags,ref pc,p,ref mc,m,IntPtr.Zero);if(code==122)continue;if(code!=0)throw new Win32Exception(code);
     var result=new NativeTopology();var monitors=Monitors();
     for(int i=0;i<mc;i++){byte[] bytes=new byte[64];Marshal.Copy(IntPtr.Add(m,i*64),bytes,0,64);result.Modes.Add(bytes);}
     for(int i=0;i<pc;i++) {
      byte[] path=new byte[72];Marshal.Copy(IntPtr.Add(p,i*72),path,0,72);result.Paths.Add(path);
      byte[] source=null,target=null;
      try { source=Request(1,84,Slice(path,0,8),U32(path,8));target=Request(2,420,Slice(path,20,8),U32(path,28)); }catch(Win32Exception){if((U32(path,68)&1)!=0)throw;continue;}
      string key=Wide(target,164,256);if(String.IsNullOrWhiteSpace(key))continue;
      var binding=new PathBinding{Key=key,Name=Wide(target,36,128),DeviceName=Wide(source,20,64),Data=path,Active=(U32(path,68)&1)!=0,Available=U32(path,60)!=0};result.Bindings.Add(binding);
      if(!binding.Active)continue;
      uint si=U32(path,12),ti=U32(path,32);if(si>=result.Modes.Count||ti>=result.Modes.Count)throw new InvalidOperationException("O driver retornou um modo de tela incompleto.");
      byte[] sm=result.Modes[(int)si],tm=result.Modes[(int)ti];if(U32(sm,0)!=1||U32(tm,0)!=2)throw new InvalidOperationException("Associação de modos não suportada nesta configuração.");
      Tuple<IntPtr,MonitorInfo> mon;bool has=monitors.TryGetValue(binding.DeviceName,out mon);
      int sw=I32(sm,16),sh=I32(sm,20);bool rotated=U32(path,40)==2||U32(path,40)==4;
      var d=new DisplayRecord{Key=key,Model=String.IsNullOrWhiteSpace(binding.Name)?"Monitor":binding.Name,DeviceName=binding.DeviceName,PathData=Convert.ToBase64String(path),SourceData=Convert.ToBase64String(sm),TargetData=Convert.ToBase64String(tm),X=has?mon.Item2.Bounds.Left:I32(sm,28),Y=has?mon.Item2.Bounds.Top:I32(sm,32),Width=has?mon.Item2.Bounds.Right-mon.Item2.Bounds.Left:rotated?sh:sw,Height=has?mon.Item2.Bounds.Bottom-mon.Item2.Bounds.Top:rotated?sw:sh,IsPrimary=has?(mon.Item2.Flags&1)!=0:I32(sm,28)==0&&I32(sm,32)==0};result.Active.Add(d);
     }
     if(result.Active.Count==0)throw new InvalidOperationException("Nenhuma tela ativa foi encontrada na sessão local.");
     return result;
    }finally{Marshal.FreeHGlobal(p);Marshal.FreeHGlobal(m);}
   }
   throw new InvalidOperationException("As telas mudaram durante a leitura. Atualize e tente novamente.");
  }
  public static DisplayRecord Preferred(PathBinding b,int x) {
   byte[] preferred=Request(3,80,Slice(b.Data,20,8),U32(b.Data,28));
   int w=I32(preferred,20),h=I32(preferred,24);if(w<1||h<1)throw new InvalidOperationException("Modo preferido indisponível.");
   byte[] source=new byte[64],target=new byte[64],path=(byte[])b.Data.Clone();
   Put(source,0,1);Array.Copy(path,0,source,8,8);Put(source,4,U32(path,8));PutInt(source,16,w);PutInt(source,20,h);Put(source,24,4);PutInt(source,28,x);PutInt(source,32,0);
   Put(target,0,2);Array.Copy(path,20,target,8,8);Put(target,4,U32(path,28));Array.Copy(preferred,32,target,16,48);
   Put(path,40,1);Put(path,44,1);Put(path,56,U32(preferred,76));Put(path,48,U32(preferred,48));Put(path,52,U32(preferred,52));
   return new DisplayRecord{Key=b.Key,Model=b.Name,DeviceName=b.DeviceName,PathData=Convert.ToBase64String(path),SourceData=Convert.ToBase64String(source),TargetData=Convert.ToBase64String(target),X=x,Y=0,Width=w,Height=h,InitialPosition=true};
  }
  public static TopologyImage Build(List<DisplayRecord> desired,List<PathBinding> available) {
   var image=new TopologyImage();var used=new HashSet<string>();
   foreach(var d in desired.OrderByDescending(d=>available.Any(p=>Same(p.Key,d.Key)&&p.Active))) {
    var candidates=available.Where(p=>Same(p.Key,d.Key)&&p.Available&&!used.Contains(p.SourceKey)).ToList();
    var original=Convert.FromBase64String(d.PathData);
    var binding=candidates.OrderByDescending(p=>p.Active).ThenByDescending(p=>p.Data.Take(12).SequenceEqual(original.Take(12))).FirstOrDefault();
    if(binding==null)throw new InvalidOperationException("Tela desconectada ou caminho indisponível: "+d.Name+". Reconecte-a e atualize.");
    used.Add(binding.SourceKey);
    byte[] path=(byte[])original.Clone(),source=Convert.FromBase64String(d.SourceData),target=Convert.FromBase64String(d.TargetData);
    if(path.Length!=72||source.Length!=64||target.Length!=64)throw new InvalidOperationException("Perfil com tamanho de estrutura inválido.");
    Array.Copy(binding.Data,0,path,0,12);Array.Copy(binding.Data,20,path,20,12);
    Array.Copy(binding.Data,0,source,8,8);Put(source,4,U32(binding.Data,8));Put(source,0,1);PutInt(source,28,d.X);PutInt(source,32,d.Y);
    Array.Copy(binding.Data,20,target,8,8);Put(target,4,U32(binding.Data,28));Put(target,0,2);
    Put(path,12,(uint)image.Modes.Count);image.Modes.Add(Convert.ToBase64String(source));Put(path,32,(uint)image.Modes.Count);image.Modes.Add(Convert.ToBase64String(target));
    Put(path,16,0);Put(path,60,1);Put(path,64,0);Put(path,68,1);image.Paths.Add(Convert.ToBase64String(path));
   }
   return image;
  }
  public static int Configure(TopologyImage image,bool validate,bool persist) {
   if(image==null||image.Paths==null||image.Modes==null||image.Paths.Count<1||image.Paths.Count>64||image.Modes.Count>192)throw new InvalidOperationException("Configuração de telas inválida.");
   byte[] paths=image.Paths.SelectMany(Convert.FromBase64String).ToArray(),modes=image.Modes.SelectMany(Convert.FromBase64String).ToArray();
   if(paths.Length!=image.Paths.Count*72||modes.Length!=image.Modes.Count*64)throw new InvalidOperationException("Estruturas de tela inválidas.");
   var p=GCHandle.Alloc(paths,GCHandleType.Pinned);var m=GCHandle.Alloc(modes,GCHandleType.Pinned);
   try {return SetDisplayConfig((uint)image.Paths.Count,p.AddrOfPinnedObject(),(uint)image.Modes.Count,m.AddrOfPinnedObject(),0x20u|(validate?0x40u:0x80u)|(persist&&!validate?0x200u:0));}
   finally{p.Free();m.Free();}
  }
  public static void Check(int code,string operation) { if(code!=0)throw new Win32Exception(code,operation+": "+new Win32Exception(code).Message+" ("+code+")"); }
 }
}
