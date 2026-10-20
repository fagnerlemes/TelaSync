using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tela {
 public static class Tests {
  static List<string> passed=new List<string>();
  static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed.Add(name);}
  static DisplayRecord D(string key,int number,int x,int y,int w,int h,uint rotation) {
   byte[] path=new byte[72],source=new byte[64],target=new byte[64];Native.Put(path,40,rotation);Native.Put(path,68,1);Native.Put(source,0,1);Native.Put(target,0,2);Native.Put(source,24,4);Native.PutInt(source,16,w);Native.PutInt(source,20,h);
   return new DisplayRecord{Key=key,Model=key,Alias=key,Number=number,X=x,Y=y,Width=w,Height=h,IsPrimary=key=="main",PathData=Convert.ToBase64String(path),SourceData=Convert.ToBase64String(source),TargetData=Convert.ToBase64String(target)};
  }
  static List<DisplayRecord> Layout(){return new List<DisplayRecord>{D("main",1,0,0,3440,1440,1),D("left",2,-1920,0,1920,1080,1),D("small",3,-2520,53,600,1024,4)};}
  static PathBinding Binding(string key,uint source,uint target,bool active) {
   var bytes=new byte[72];Native.Put(bytes,0,123);Native.Put(bytes,8,source);Native.Put(bytes,20,123);Native.Put(bytes,28,target);Native.Put(bytes,60,1);Native.Put(bytes,68,active?1u:0);
   return new PathBinding{Key=key,Data=bytes,Available=true,Active=active,Name=key,DeviceName="DISPLAY"+source};
  }
  public static object Run(string tempRoot) {
   passed.Clear();var memory=Layout();string baseline=LayoutPlanner.Fingerprint(memory);
   var all=LayoutPlanner.Plan(memory,new[]{"main","left","small"},"main");Check(LayoutPlanner.Fingerprint(all)==baseline,"all active: exact positions, negative X and vertical offset");
   var middleOff=LayoutPlanner.Plan(memory,new[]{"main","small"},"main");Check(middleOff.First(d=>d.Key=="small").X==-600,"middle disabled: close temporary gap to the left");Check(middleOff.First(d=>d.Key=="small").Y==53,"middle disabled: retain vertical offset");Check(LayoutPlanner.Fingerprint(memory)==baseline,"compaction never overwrites remembered positions");
   Check(LayoutPlanner.Fingerprint(LayoutPlanner.Plan(memory,new[]{"small","main","left"},"main"))==baseline,"reactivation order does not change final layout");
   var noPrimary=LayoutPlanner.Plan(memory,new[]{"left","small"},"main");Check(noPrimary.Any(d=>d.X==0&&d.Y==0),"primary disabled: temporary primary at origin");Check(LayoutPlanner.Fingerprint(LayoutPlanner.Plan(memory,new[]{"main","left","small"},"main"))==baseline,"original primary reactivated: restore full arrangement");
   bool caught=false;try{LayoutPlanner.Plan(memory,new string[0],"main");}catch(InvalidOperationException){caught=true;}Check(caught,"reject disabling the final monitor");
   caught=false;try{LayoutPlanner.Plan(memory,new[]{"missing"},"main");}catch(InvalidOperationException){caught=true;}Check(caught,"reject unknown identity rather than choose another monitor");
   var leftOnly=LayoutPlanner.Plan(memory,new[]{"left"},"main");Check(leftOnly[0].X==0&&leftOnly[0].Y==0,"single nonprimary screen normalized");
   var roundtrip=Json.Clone(new AppState{Remembered=memory,PrimaryKey="main",LastAppliedFingerprint=baseline});Check(LayoutPlanner.Fingerprint(roundtrip.Remembered)==baseline,"JSON survives restart including coordinates and rotation");
   Check(LayoutPlanner.Rotation(roundtrip.Remembered.First(d=>d.Key=="small"))==4,"rotated source retained separately from desktop dimensions");
   var bindings=new List<PathBinding>{Binding("main",9,300,true),Binding("left",6,200,false),Binding("small",7,100,true)};var image=Native.Build(memory,bindings);Check(image.Paths.Count==3&&image.Modes.Count==6,"build complete topology with source and target modes");
   Check(image.Paths.Select(Convert.FromBase64String).Any(p=>Native.U32(p,8)==6&&Native.U32(p,28)==200),"rebind identity to current source and target IDs");
   var leftPath=Convert.FromBase64String(image.Paths.First(p=>Native.U32(Convert.FromBase64String(p),28)==200));var leftSource=Convert.FromBase64String(image.Modes[(int)Native.U32(leftPath,12)]);Check(Native.I32(leftSource,28)==-1920&&Native.U32(leftSource,4)==6,"remapped source preserves remembered X and source metadata");
   var smallPath=Convert.FromBase64String(image.Paths.First(p=>Native.U32(Convert.FromBase64String(p),28)==100));Check(Native.U32(smallPath,40)==4,"rebinding preserves target rotation");
   caught=false;try{Native.Build(memory,bindings.Where(b=>b.Key!="left").ToList());}catch(InvalidOperationException){caught=true;}Check(caught,"disconnected target aborts instead of applying partial profile");
   Check(PowerPolicy.Values("vcp(10 D6(01 04 05) DF)").SequenceEqual(new uint[]{1,4,5}),"parse MCCS power values");Check(PowerPolicy.OffValue(new List<uint>{1,2,4})==2,"prefer announced standby");Check(PowerPolicy.OffValue(new List<uint>{1,4,5})==4,"use announced off mode, never write-only value 5");Check(PowerPolicy.OffValue(new List<uint>{1,5})==0,"reject unvalidated write-only power mode");
   using(var fake=new FakePower("left",true)) {var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(report.Success&&fake.Sent.SequenceEqual(new uint[]{4}),"only selected physical backend receives sleep command");Check(report.TopologyUnchanged,"power command preserves desktop fingerprint");}
   using(var fake=new FakePower("left",false)) {var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(!report.Success&&fake.Sent.Count==0,"unsupported power sends nothing and has no global fallback");}
   using(var fake=new FakePower("left",false)){var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="wake"},fake);Check(report.Success&&fake.Sent.SequenceEqual(new uint[]{1}),"wake may be requested even if asleep monitor cannot report capabilities");}
   using(var fake=new FakePower("main",true)){caught=false;try{PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);}catch(InvalidOperationException){caught=true;}Check(caught&&fake.Sent.Count==0,"mismatched physical identity blocks power writes");}
   using(var fake=new FakePower("left",true)){fake.ChangeTopology=true;var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(!report.Success&&!report.Supported&&fake.Sent.SequenceEqual(new uint[]{4,1}),"connection loss triggers wake of same panel and marks method incompatible");}
   using(var fake=new FakePower("left",true)){PowerPolicy.Execute(new PowerRequest{Key="left",Action="probe"},fake);Check(fake.Sent.Count==0,"capability probe is read-only");}
   string dir=Path.Combine(Path.GetFullPath(tempRoot),"tela-test-"+Guid.NewGuid().ToString("N"));
   try {
    var store=new StateStore(dir);store.Save(roundtrip);var restarted=store.Load();Check(LayoutPlanner.Fingerprint(restarted.Remembered)==baseline,"saved memory survives a fresh store instance");
    restarted.DarkTheme=false;store.Save(restarted);Check(File.Exists(store.StatePath+".bak")&&!store.Load().DarkTheme,"atomic state replacement keeps previous version");
   }finally {if(Directory.Exists(dir)){foreach(var file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}}
   return new {Passed=passed.Count,Failed=0,Tests=passed,HardwareWrites=0,Note="Native API validation and UI rendering are separate read-only checks."};
  }
  sealed class FakePower:IPowerBackend {
   public string Key{get;private set;}readonly bool support;public List<uint> Sent=new List<uint>();public bool ChangeTopology;
   public FakePower(string key,bool supported){Key=key;support=supported;}
   public PowerReport Probe(){return new PowerReport{Key=Key,Success=true,Supported=support,OffValue=support?4u:0,Message="Fake"};}
   public void Send(uint value){Sent.Add(value);}
   public string Fingerprint(){return ChangeTopology&&Sent.Count>0?"changed":"stable";}
   public void Dispose(){}
  }
 }
}
