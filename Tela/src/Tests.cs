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
   Check(PowerPolicy.Values("vcp(10 D6(01 04 05) DF)").SequenceEqual(new uint[]{1,4,5}),"parse MCCS power values");Check(PowerPolicy.OffValue(new List<uint>{1,2,4})==2,"prefer standby over a deeper off state");Check(PowerPolicy.OffValue(new List<uint>{1,4,5})==4,"physical hard-off mode 5 is never selected");Check(PowerPolicy.OffValue(new List<uint>{1,5})==0,"hard-off alone does not count as safe sleep support");Check(PowerPolicy.OffValue(new List<uint>{1,3,4})==3,"prefer advertised suspend before off");Check(PowerPolicy.OffValue(new List<uint>{4,5})==0,"require an advertised return-on state");
   using(var fake=new FakePower("left",true)) {var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(report.Success&&fake.Sent.SequenceEqual(new uint[]{4}),"only selected physical backend receives sleep command");Check(report.TopologyUnchanged,"power command preserves desktop fingerprint");}
   using(var fake=new FakePower("left",false)) {var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(!report.Success&&fake.Sent.Count==0,"unsupported power sends nothing and has no global fallback");}
   using(var fake=new FakePower("left",false)){var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="wake"},fake);Check(report.Success&&fake.Sent.SequenceEqual(new uint[]{1}),"wake may be requested even if asleep monitor cannot report capabilities");}
   using(var fake=new FakePower("main",true)){caught=false;try{PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);}catch(InvalidOperationException){caught=true;}Check(caught&&fake.Sent.Count==0,"mismatched physical identity blocks power writes");}
   using(var fake=new FakePower("left",true)){fake.ChangeTopology=true;var report=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(!report.Success&&!report.Supported&&fake.Sent.SequenceEqual(new uint[]{4,1}),"connection loss triggers wake of same panel and marks method incompatible");}
   using(var fake=new FakePower("left",true)){PowerPolicy.Execute(new PowerRequest{Key="left",Action="probe"},fake);Check(fake.Sent.Count==0,"capability probe is read-only");}
   using(var fake=new FakePower("device#LHCFFFF#port",true)){var r=PowerPolicy.Execute(new PowerRequest{Key=fake.Key,Action="sleep"},fake);Check(!r.Success&&!r.Supported&&fake.Sent.Count==0,"known self-waking Blizzard panel receives no sleep or hard-off writes");}
   using(var fake=new FakePower("device#LHCFFFF#port",true)){var r=PowerPolicy.Execute(new PowerRequest{Key=fake.Key,Action="wake"},fake);Check(r.Success&&fake.Sent.SequenceEqual(new uint[]{1}),"known sleep incompatibility does not block wake");}
   using(var fake=new FakePower("left",true)){fake.InvalidMode=true;var r=PowerPolicy.Execute(new PowerRequest{Key="left",Action="sleep"},fake);Check(!r.Success&&fake.Sent.Count==0,"policy blocks hard-off even if a backend incorrectly offers it");}
   var rotationEditor=new LayoutEditor(memory,"main");string sourceBefore=rotationEditor.Find("left").SourceData;rotationEditor.Rotate("left",1);var rotated=rotationEditor.Find("left");Check(rotated.Width==1080&&rotated.Height==1920&&Native.U32(Convert.FromBase64String(rotated.PathData),40)==2,"clockwise draft swaps bounds and rotates target 90 degrees");Check(rotated.SourceData==sourceBefore&&rotationEditor.Find("small").PathData==memory.First(d=>d.Key=="small").PathData,"rotation preserves physical source timing and the Intel monitor");
   rotationEditor.Rotate("left",-1);Check(LayoutPlanner.Fingerprint(rotationEditor.Displays)==baseline,"opposite rotations return to the exact original arrangement");rotationEditor.Rotate("left",-1);Check(Native.U32(Convert.FromBase64String(rotationEditor.Find("left").PathData),40)==4,"counterclockwise rotation wraps from zero to 270 degrees");for(int i=0;i<3;i++)rotationEditor.Rotate("left",-1);Check(LayoutPlanner.Fingerprint(rotationEditor.Displays)==baseline,"four quarter turns preserve all positions and orientations");Check(LayoutPlanner.Fingerprint(memory)==baseline,"rotation never modifies saved memory before confirmation");
   var editor=new LayoutEditor(memory,"main");editor.Move("left",3445,3);editor.Snap("left",10);Check(editor.Find("left").X==3440&&editor.Find("left").Y==0,"layout editor snaps screen edges and vertical alignment");Check(LayoutPlanner.Fingerprint(memory)==baseline,"layout draft never changes saved memory before confirmation");
   var deskRecords=Layout();var television=D("tv",4,3440,0,3840,2160,1);television.Model="SAMSUNG";deskRecords.Add(television);var deskState=new AppState{Remembered=deskRecords,PrimaryKey="main",TvKey="tv"};
   string deskFingerprint=LayoutPlanner.Fingerprint(deskRecords);var tvState=TvModePlan.Start(deskState,Layout(),"tv");var onlyTv=LayoutPlanner.Plan(tvState.Remembered,new[]{"tv"},"tv");
   Check(onlyTv.Count==1&&onlyTv[0].IsPrimary&&onlyTv[0].X==0&&onlyTv[0].Y==0,"TV mode enables only the television at the primary origin");
   Check(tvState.BeforeTv.Enabled.Count==3&&!tvState.BeforeTv.Enabled.Contains("tv")&&tvState.BeforeTv.PrimaryKey=="main","TV mode snapshots three active desk displays and their original primary");
   Check(deskState.BeforeTv==null&&LayoutPlanner.Fingerprint(tvState.Remembered)==deskFingerprint,"temporary TV-only layout never replaces canonical positions or mutates the original state");
   var restoredDesk=TvModePlan.Restore(Json.Clone(tvState));var deskPlan=LayoutPlanner.Plan(restoredDesk.Remembered,tvState.BeforeTv.Enabled,restoredDesk.PrimaryKey);Check(restoredDesk.BeforeTv==null&&LayoutPlanner.Fingerprint(deskPlan)==baseline,"return from TV restores exactly the previous three-display arrangement after serialization");
   var fourState=TvModePlan.Start(deskState,deskRecords,"tv");var fourRestored=TvModePlan.Restore(fourState);Check(fourState.BeforeTv.Enabled.Count==4&&LayoutPlanner.Fingerprint(LayoutPlanner.Plan(fourRestored.Remembered,fourState.BeforeTv.Enabled,fourRestored.PrimaryKey))==deskFingerprint,"return restores all four when the TV was already active before gaming");
   caught=false;try{TvModePlan.Start(tvState,onlyTv,"tv");}catch(InvalidOperationException){caught=true;}Check(caught&&tvState.BeforeTv.Enabled.Count==3,"re-entering TV mode cannot overwrite the return snapshot");
   Check(TvModePlan.ChooseTarget(deskRecords,null)=="tv"&&TvModePlan.ChooseTarget(deskRecords,"left")=="left","TV preference uses the named Samsung television and respects explicit selection");
   var ambiguous=deskRecords.Select(d=>d.Copy()).ToList();ambiguous.First(d=>d.Key=="left").Model="SAMSUNG";Check(TvModePlan.ChooseTarget(ambiguous,null)==null,"ambiguous television names require a choice instead of an arbitrary target");
   var tvBindings=bindings.Concat(new[]{Binding("tv",11,500,false)}).ToList();var tvImage=Native.Build(onlyTv,tvBindings);Check(tvImage.Paths.Count==1&&Native.U32(Convert.FromBase64String(tvImage.Paths[0]),28)==500,"TV-only native plan contains no desktop-monitor paths");
   caught=false;try{Native.Build(deskPlan,tvBindings.Where(b=>b.Key!="left").ToList());}catch(InvalidOperationException){caught=true;}Check(caught&&tvState.BeforeTv!=null&&tvState.BeforeTv.Enabled.Count==3,"unavailable monitor aborts return without losing the saved TV session");
   editor.Number("left",1);Check(editor.Find("main").Number==2&&editor.Find("left").Number==1,"renumbering swaps labels without changing monitor identity");editor.MakePrimary("left");var normalized=editor.Normalize();Check(normalized.First(d=>d.Key=="left").X==0&&normalized.First(d=>d.Key=="main").X==-3440,"primary selection normalizes all saved positions");
   editor.Move("small",100,100);editor.Snap("small",0);Check(!editor.Displays.Where(d=>d.Key!="small").Any(d=>LayoutPlanner.Overlap(d,editor.Find("small"))),"drop resolves overlap without stacking monitors");
   using(var fake=new FakeBrightness("left",true)){var r=PowerPolicy.Execute(new PowerRequest{Key="left",Action="brightness-set",BrightnessPercent=25},fake);Check(r.Success&&fake.Writes.SequenceEqual(new uint[]{64})&&fake.PowerWrites==0,"brightness scales percent to hardware max and never sends power commands");}
   using(var fake=new FakeBrightness("left",false)){var r=PowerPolicy.Execute(new PowerRequest{Key="left",Action="brightness-set",BrightnessPercent=50},fake);Check(!r.Success&&fake.Writes.Count==0,"unsupported brightness performs no writes");}
   using(var fake=new FakeBrightness("left",true)){caught=false;try{PowerPolicy.Execute(new PowerRequest{Key="left",Action="brightness-set",BrightnessPercent=101},fake);}catch(ArgumentException){caught=true;}Check(caught&&fake.Writes.Count==0,"brightness rejects percentages above range");}
   using(var fake=new FakeBrightness("main",true)){caught=false;try{PowerPolicy.Execute(new PowerRequest{Key="left",Action="brightness-set",BrightnessPercent=50},fake);}catch(InvalidOperationException){caught=true;}Check(caught&&fake.Writes.Count==0,"brightness rejects a changed physical identity");}
   using(var fake=new FakeBrightness("left",true)){PowerPolicy.Execute(new PowerRequest{Key="left",Action="brightness-probe"},fake);Check(fake.Writes.Count==0,"brightness probe does not change the panel");}
   var startup=new FakeStartup();string app=Path.Combine(Path.GetFullPath(tempRoot),"TelaSync.exe");StartupPolicy.Set(startup,app,true);Check(startup.Value==Program.Quote(app)+" --start-minimized","startup uses quoted executable path and tray argument");StartupPolicy.Set(startup,app,false);Check(startup.Value==null,"startup can be disabled without affecting other entries");startup.Value="another-program.exe";caught=false;try{StartupPolicy.Set(startup,app,false);}catch(InvalidOperationException){caught=true;}Check(caught&&startup.Value=="another-program.exe","startup preserves an unrecognized existing entry");
   var closeState=new AppState();Check(ClosePolicy.Action(closeState)==CloseChoice.Cancel,"legacy and new states ask before closing by default");ClosePolicy.Remember(closeState,CloseChoice.Tray,false);Check(closeState.AskBeforeClose,"an unchecked remember box does not change close preferences");ClosePolicy.Remember(closeState,CloseChoice.Cancel,true);Check(closeState.AskBeforeClose,"cancelling the close dialog never saves a choice");ClosePolicy.Remember(closeState,CloseChoice.Tray,true);Check(ClosePolicy.Action(closeState)==CloseChoice.Tray,"remembered tray choice skips subsequent dialogs");closeState.AskBeforeClose=true;Check(ClosePolicy.Action(closeState)==CloseChoice.Cancel,"preferences can restore the close question");ClosePolicy.Remember(closeState,CloseChoice.Exit,true);Check(ClosePolicy.Action(closeState)==CloseChoice.Exit,"remembered quit is distinct from minimizing to the tray");var invalidClose=new AppState{AskBeforeClose=false,RememberedCloseChoice=(CloseChoice)99};Check(ClosePolicy.Action(invalidClose)==CloseChoice.Tray,"invalid saved close action safely falls back to the tray");
   string dir=Path.Combine(Path.GetFullPath(tempRoot),"tela-test-"+Guid.NewGuid().ToString("N"));
   try {
    var store=new StateStore(dir);store.Save(roundtrip);var restarted=store.Load();Check(LayoutPlanner.Fingerprint(restarted.Remembered)==baseline,"saved memory survives a fresh store instance");
    restarted.DarkTheme=false;store.Save(restarted);Check(File.Exists(store.StatePath+".bak")&&!store.Load().DarkTheme,"atomic state replacement keeps previous version");
    store.Save(tvState);var tvRestart=store.Load();Check(tvRestart.BeforeTv!=null&&tvRestart.BeforeTv.Enabled.Count==3&&TvModePlan.Restore(tvRestart).PrimaryKey=="main","return from TV remains available after closing and reopening the application");
    tvRestart.AskBeforeClose=false;tvRestart.RememberedCloseChoice=CloseChoice.Tray;store.Save(tvRestart);var closeRestart=store.Load();Check(ClosePolicy.Action(closeRestart)==CloseChoice.Tray&&closeRestart.BeforeTv!=null,"tray preference and TV return snapshot survive the same state-file restart");
   }finally {if(Directory.Exists(dir)){foreach(var file in Directory.GetFiles(dir))File.Delete(file);Directory.Delete(dir);}}
   return new {Passed=passed.Count,Failed=0,Tests=passed,HardwareWrites=0,Note="Native API validation and UI rendering are separate read-only checks."};
  }
  sealed class FakePower:IPowerBackend {
   public string Key{get;private set;}readonly bool support;public List<uint> Sent=new List<uint>();public bool ChangeTopology,InvalidMode;
   public FakePower(string key,bool supported){Key=key;support=supported;}
   public PowerReport Probe(){return new PowerReport{Key=Key,Success=true,Supported=support,OffValue=InvalidMode?5u:support?4u:0,Message="Fake"};}
   public void Send(uint value){Sent.Add(value);}
   public string Fingerprint(){return ChangeTopology&&Sent.Count>0?"changed":"stable";}
   public void Dispose(){}
  }
  sealed class FakeStartup:IStartupStore{public string Value;public string Read(){return Value;}public void Write(string value){Value=value;}public void Delete(){Value=null;}}
  sealed class FakeBrightness:IPowerBackend,IBrightnessBackend {
   public string Key{get;private set;}readonly bool support;public List<uint> Writes=new List<uint>();public int PowerWrites;
   public FakeBrightness(string key,bool supported){Key=key;support=supported;}
   public PowerReport ReadBrightness(){return new PowerReport{Key=Key,Success=support,Supported=support,BrightnessMaximum=255,BrightnessPercent=50};}
   public void SendBrightness(uint value){Writes.Add(value);}public PowerReport Probe(){throw new Exception("Brightness must not query power");}public void Send(uint value){PowerWrites++;}public string Fingerprint(){return "stable";}public void Dispose(){}
  }
 }
}
