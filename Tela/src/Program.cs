using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly:AssemblyTitle(Tela.ProductInfo.Name)]
[assembly:AssemblyCompany(Tela.ProductInfo.Publisher)]
[assembly:AssemblyProduct(Tela.ProductInfo.Name)]
[assembly:AssemblyInformationalVersion(Tela.ProductInfo.Version)]
[assembly:AssemblyCopyright("2026 Fagner Lemes")]
[assembly:AssemblyDescription("Gerenciador de monitores com memória de posição")]
[assembly:AssemblyVersion(Tela.ProductInfo.AssemblyVersion)]
[assembly:AssemblyFileVersion(Tela.ProductInfo.AssemblyVersion)]
namespace Tela {
 public static class Program {
  public static string Quote(string path){if(path.IndexOf('"')>=0)throw new ArgumentException("Caminho inválido.");return "\""+path+"\"";}
  static string Option(string[] args,string name){int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:null;}
  [STAThread]public static int Main(string[] args) {
   try {
    if(args.Length>=2&&args[0]=="--power-worker")return PowerWorker(args[1]);
    if(args.Length>=2&&args[0]=="--watchdog")return RecoveryGuard.Watch(args[1]);
    if(args.Length>=2&&args[0]=="--self-test"){Json.Write(args[1],Tests.Run(Path.GetDirectoryName(Path.GetFullPath(args[1]))));return 0;}
    if(args.Length>=2&&args[0]=="--probe"){
     var all=Native.Capture(true);Json.Write(args[1],new{Version=ProductInfo.Version,ReadOnly=true,Active=all.Active,Connected=all.Bindings.Where(p=>p.Available).GroupBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(g=>new{Key=g.Key,Model=g.First().Name,Active=g.Any(p=>p.Active)}).ToList()});return 0;
    }
    if(args.Length>=2&&args[0]=="--validate-current"){
     var live=Native.Capture(false);var all=Native.Capture(true);var built=Native.Build(live.Active,all.Bindings);int original=Native.Configure(live.Image(),true,false),rebuilt=Native.Configure(built,true,false);
     Json.Write(args[1],new{ReadOnly=true,Flags="SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG",OriginalValidationCode=original,RebuiltValidationCode=rebuilt,DisplayCount=live.Active.Count});return original==0&&rebuilt==0?0:2;
    }
    if(args.Length>=2&&args[0]=="--validate-plans"){
     var live=Native.Capture(true);string primary=live.Active.First(d=>d.IsPrimary).Key;var results=new System.Collections.Generic.List<object>();bool passed=true;
     foreach(var disabled in live.Active){var keys=live.Active.Where(d=>!Native.Same(d.Key,disabled.Key)).Select(d=>d.Key).ToList();if(keys.Count==0)continue;var plan=LayoutPlanner.Plan(live.Active,keys,primary);int code=Native.Configure(Native.Build(plan,live.Bindings),true,false);passed&=code==0;results.Add(new{Disabled=disabled.Model,Code=code,Planned=plan.Select(d=>new{d.Model,d.X,d.Y,d.Width,d.Height}).ToList()});}
     foreach(var binding in live.Bindings.Where(b=>b.Available&&!b.Active&&!live.Active.Any(d=>Native.Same(d.Key,b.Key))).GroupBy(b=>b.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First())){var records=live.Active.Select(d=>d.Copy()).ToList();records.Add(Native.Preferred(binding,records.Max(d=>d.Right)));var plan=LayoutPlanner.Plan(records,records.Select(d=>d.Key),primary);int code=Native.Configure(Native.Build(plan,live.Bindings),true,false);passed&=code==0;results.Add(new{Enabled=binding.Name,Code=code,Note="Preferred mode validation only"});}
     Json.Write(args[1],new{ReadOnly=true,HardwareWrites=0,Cases=results});return passed?0:2;
    }
    if(args.Length>=2&&args[0]=="--test-cancelled-watchdog"){
     string directory=Path.GetDirectoryName(Path.GetFullPath(args[1]));string file=Path.Combine(directory,"cancelled-watchdog.json");
     Json.Write(file,new RecoveryRecord{Id=Guid.NewGuid().ToString("N"),DeadlineTicks=DateTime.UtcNow.AddSeconds(3).Ticks,Before=new TopologyImage()});File.WriteAllText(file+".status","cancelled");
     using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,Arguments="--watchdog "+Quote(file),UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})){
      if(!child.WaitForExit(5000)){child.Kill();throw new TimeoutException("O processo auxiliar não encerrou.");}Json.Write(args[1],new{ChildExitCode=child.ExitCode,Ready=File.Exists(file+".ready"),Status=File.ReadAllText(file+".status"),HardwareWrites=0});return child.ExitCode;
     }
    }
    if(args.Length>=2&&args[0]=="--test-confirmed-watchdog"){
     string directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"confirmed-watchdog");var guard=new RecoveryGuard(directory,new TopologyImage());string marker=guard.RecordPath+".persisted";guard.Commit(()=>File.WriteAllText(marker,"saved"));var record=Json.Read<RecoveryRecord>(guard.RecordPath);record.DeadlineTicks=DateTime.UtcNow.AddSeconds(-1).Ticks;Json.Write(guard.RecordPath,record);
     using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,Arguments="--watchdog "+Quote(guard.RecordPath),UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})){
      if(!child.WaitForExit(5000)){child.Kill();throw new TimeoutException("A recuperação confirmada não encerrou.");}Json.Write(args[1],new{ChildExitCode=child.ExitCode,Status=File.ReadAllText(guard.RecordPath+".status"),Persisted=File.Exists(marker),DeadlineAlreadyPassed=true,HardwareWrites=0});return child.ExitCode;
     }
    }
    if(args.Length>=2&&args[0]=="--validate-editor"){
     var live=Native.Capture(true);string primary=live.Active.First(d=>d.IsPrimary).Key;var other=live.Active.Where(d=>!d.IsPrimary).OrderByDescending(d=>d.Width).FirstOrDefault();var cases=new System.Collections.Generic.List<object>();bool passed=true;
     if(other!=null){var editor=new LayoutEditor(live.Active,primary);editor.Move(other.Key,live.Active.Max(d=>d.Right),0);editor.Snap(other.Key,10);var plan=LayoutPlanner.Plan(editor.Normalize(),live.Active.Select(d=>d.Key),editor.PrimaryKey);int position=Native.Configure(Native.Build(plan,live.Bindings),true,false);passed&=position==0;cases.Add(new{Case="move secondary to right",Code=position});editor=new LayoutEditor(live.Active,primary);editor.MakePrimary(other.Key);plan=LayoutPlanner.Plan(editor.Normalize(),live.Active.Select(d=>d.Key),editor.PrimaryKey);int main=Native.Configure(Native.Build(plan,live.Bindings),true,false);passed&=main==0;cases.Add(new{Case="change primary monitor",Code=main});}
     Json.Write(args[1],new{ReadOnly=true,HardwareWrites=0,Cases=cases});return passed?0:2;
    }
    if(args.Length>=2&&args[0]=="--validate-rotation"){
     var live=Native.Capture(true);string primary=live.Active.First(d=>d.IsPrimary).Key;var cases=new System.Collections.Generic.List<object>();bool passed=true;
     foreach(var display in live.Active)foreach(int direction in new[]{-1,1}){
      var editor=new LayoutEditor(live.Active,primary);editor.Rotate(display.Key,direction);var plan=LayoutPlanner.Plan(editor.Normalize(),live.Active.Select(d=>d.Key),primary);int code=Native.Configure(Native.Build(plan,live.Bindings),true,false);passed&=code==0;cases.Add(new{Monitor=display.Model,Direction=direction,Rotation=Native.U32(Convert.FromBase64String(editor.Find(display.Key).PathData),40),Code=code});
     }
     Json.Write(args[1],new{ReadOnly=true,HardwareWrites=0,Cases=cases});return passed?0:2;
    }
    if(args.Length>=2&&args[0]=="--validate-tv-mode"){
     var live=Native.Capture(true);var records=live.Active.Select(d=>d.Copy()).ToList();foreach(var binding in live.Bindings.Where(b=>b.Available&&!records.Any(d=>Native.Same(d.Key,b.Key))).GroupBy(b=>b.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()))records.Add(Native.Preferred(binding,records.Max(d=>d.Right)));
     string key=TvModePlan.ChooseTarget(records,null);if(key==null)throw new InvalidOperationException("TV não identificada sem ambiguidades para a validação.");var initial=new AppState{Remembered=records,PrimaryKey=live.Active.First(d=>d.IsPrimary).Key,TvKey=key};string baseline=LayoutPlanner.Fingerprint(records);var tvState=TvModePlan.Start(initial,live.Active,key);var only=LayoutPlanner.Plan(tvState.Remembered,new[]{key},key);int tvCode=Native.Configure(Native.Build(only,live.Bindings),true,false);
     var restored=TvModePlan.Restore(tvState);var back=LayoutPlanner.Plan(restored.Remembered,tvState.BeforeTv.Enabled,restored.PrimaryKey);int returnCode=Native.Configure(Native.Build(back,live.Bindings),true,false);var mainEditor=new LayoutEditor(records,key);var primaryPlan=LayoutPlanner.Plan(mainEditor.Normalize(),live.Active.Select(d=>d.Key).Concat(new[]{key}),key);int primaryCode=Native.Configure(Native.Build(primaryPlan,live.Bindings),true,false);
     var service=new MonitorService(args[1]+".data");service.State=initial;var enterChange=service.PrepareTvMode();service.State=enterChange.NewState;var returnChange=service.PrepareReturnFromTv();
     bool preserved=LayoutPlanner.Fingerprint(tvState.Remembered)==baseline;bool manualReturn=!enterChange.RequireConfirmation&&!returnChange.RequireConfirmation;Json.Write(args[1],new{ReadOnly=true,HardwareWrites=0,Television=records.First(d=>d.Key==key).Name,TvOnlyValidationCode=tvCode,ReturnValidationCode=returnCode,PrimaryValidationCode=primaryCode,OnlyTvIsPrimary=only.Count==1&&only[0].IsPrimary,ReturnDisplayCount=back.Count,CanonicalMemoryPreserved=preserved,EnterRequiresConfirmation=enterChange.RequireConfirmation,ReturnRequiresConfirmation=returnChange.RequireConfirmation});return tvCode==0&&returnCode==0&&primaryCode==0&&preserved&&manualReturn?0:2;
    }
    string data=Option(args,"--data-dir")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),ProductInfo.AppId);
    if(args.Length>=2&&args[0]=="--ui-smoke"){
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var desktop=new DesktopApp(data,true);Json.Write(args[1],desktop.SmokeTest());app.Shutdown();return 0;
    }
    if(args.Length>=2&&args[0]=="--render-close-preview"){
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var desktop=new DesktopApp(data,true);if(Option(args,"--theme")=="light")desktop.PreviewTheme(false);desktop.RenderClosePreview(args[1]);app.Shutdown();return 0;
    }
    if(args.Length>=2&&args[0]=="--render-preview"){
     var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var desktop=new DesktopApp(data,true);if(Option(args,"--theme")=="light")desktop.PreviewTheme(false);desktop.PreviewPage(Option(args,"--view"));desktop.RenderPreview(args[1]);app.Shutdown();return 0;
    }
    if(args.Length>0&&args[0]!="--data-dir"&&args[0]!="--start-minimized")throw new ArgumentException("Opção desconhecida. Use "+ProductInfo.Executable+" para abrir a interface.");
    bool created;using(var mutex=new Mutex(true,"Local\\"+ProductInfo.AppId+"Desktop",out created)) {
     if(!created){NativeTheme.RequestShow();return 0;}
     var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var desktop=new DesktopApp(data,false);app.SessionEnding+=(s,e)=>desktop.AllowExit();if(args.Contains("--start-minimized"))desktop.Window.Loaded+=(s,e)=>desktop.Window.Dispatcher.BeginInvoke(new Action(desktop.StartInTray));app.DispatcherUnhandledException+=(s,e)=>{try{File.AppendAllText(Path.Combine(data,"falhas.log"),e.Exception+Environment.NewLine);}catch(IOException){}MessageBox.Show(e.Exception.Message,ProductInfo.Name,MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};return app.Run(desktop.Window);
    }
   }catch(Exception e){
    if(args.Length>=2){try{File.WriteAllText(args[1]+".error.txt",e.ToString());}catch(Exception){}}
    else MessageBox.Show(e.Message,"Não foi possível iniciar o "+ProductInfo.Name,MessageBoxButton.OK,MessageBoxImage.Error);
    return 1;
   }
  }
  static int PowerWorker(string file) {
   var request=Json.Read<PowerRequest>(file);PowerReport result;
   try {using(var backend=new NativePower(request.Key))result=PowerPolicy.Execute(request,backend);}
   catch(Exception e){result=new PowerReport{Key=request.Key,Success=false,Supported=false,Message=e.Message};}
   Json.Write(file+".result",result);return 0;
  }
 }
}
