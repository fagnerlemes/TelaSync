using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Tela {
 public sealed class RecoveryRecord { public string Id;public long DeadlineTicks;public TopologyImage Before; }
 public sealed class RecoveryGuard {
  public readonly string RecordPath;
  readonly RecoveryRecord record;
  public RecoveryGuard(string directory,TopologyImage before) {
   record=new RecoveryRecord{Id=Guid.NewGuid().ToString("N"),Before=before,DeadlineTicks=DateTime.UtcNow.AddSeconds(50).Ticks};
   RecordPath=Path.Combine(directory,"recovery",record.Id+".json");Json.Write(RecordPath,record);File.WriteAllText(RecordPath+".status","pending");
   using(var child=Process.Start(new ProcessStartInfo{FileName=Process.GetCurrentProcess().MainModule.FileName,Arguments="--watchdog "+Program.Quote(RecordPath),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
    var timer=Stopwatch.StartNew();while(!File.Exists(RecordPath+".ready")&&timer.ElapsedMilliseconds<4000&&!child.HasExited)Thread.Sleep(30);
    if(!File.Exists(RecordPath+".ready")){File.WriteAllText(RecordPath+".status","cancelled");throw new InvalidOperationException("A recuperação independente não iniciou. Nenhuma mudança foi aplicada.");}
   }
  }
  public void Commit(Action persist) {
   using(var mutex=new Mutex(false,"Local\\TelaRecovery"+record.Id)) {
    if(!mutex.WaitOne(5000))throw new TimeoutException("A recuperação está em andamento.");
    try {
     if(File.ReadAllText(RecordPath+".status")!="pending"||DateTime.UtcNow.Ticks>=record.DeadlineTicks)throw new InvalidOperationException("O prazo de confirmação terminou. A configuração anterior foi solicitada.");
     persist();File.WriteAllText(RecordPath+".status","confirmed");
    }finally {mutex.ReleaseMutex();}
   }
  }
  public void Revert() {Restore(RecordPath,record);}
  static void Restore(string path,RecoveryRecord data) {
   using(var mutex=new Mutex(false,"Local\\TelaRecovery"+data.Id)) {
    bool acquired=false;try {
     try{acquired=mutex.WaitOne(5000);}catch(AbandonedMutexException){acquired=true;}
     if(!acquired)throw new TimeoutException("A recuperação está ocupada. A reversão ainda não foi confirmada.");
     string status=File.ReadAllText(path+".status");if(status!="pending"&&status!="restore-failed")return;
     int code=Native.Configure(data.Before,false,true);File.WriteAllText(path+".status",code==0?"reverted":"restore-failed");
     File.AppendAllText(path+".log",DateTimeOffset.Now.ToString("o")+" restore="+code+Environment.NewLine);
     Native.Check(code,"Falha ao restaurar a configuração anterior");
    }finally{if(acquired)mutex.ReleaseMutex();}
   }
  }
  public static int Watch(string path) {
   var data=Json.Read<RecoveryRecord>(path);if(data.Before==null||String.IsNullOrEmpty(data.Id))return 2;
   File.WriteAllText(path+".ready","ready");
   while(DateTime.UtcNow.Ticks<data.DeadlineTicks) {
    string status=File.ReadAllText(path+".status");if(status=="confirmed"||status=="cancelled"||status=="reverted")return 0;
    Thread.Sleep(250);
   }
   Restore(path,data);return 0;
  }
 }
}
