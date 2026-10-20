using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Tela {
 public sealed class PowerRequest { public string Key,Action;public int BrightnessPercent=-1; }
 public sealed class PowerReport {
  public string Key,Message,Capabilities;
  public bool Success,Supported,RequestedOff,TopologyUnchanged=true;
  public uint OffValue,CurrentValue,BrightnessMaximum;
  public int BrightnessPercent;
  public List<uint> Values=new List<uint>();
 }
 public interface IPowerBackend:IDisposable {
  string Key {get;}
  PowerReport Probe();
  void Send(uint value);
  string Fingerprint();
 }
 public interface IBrightnessBackend {PowerReport ReadBrightness();void SendBrightness(uint value);}
 public static class BrightnessPolicy {
  public static PowerReport Execute(PowerRequest request,IPowerBackend backend){
   if(!Native.Same(request.Key,backend.Key))throw new InvalidOperationException("A tela selecionada mudou. Nenhum comando de brilho foi enviado.");
   var brightness=backend as IBrightnessBackend;if(brightness==null)throw new InvalidOperationException("Brilho individual indisponível.");
   var report=brightness.ReadBrightness();if(request.Action=="brightness-probe"||!report.Supported)return report;
   if(request.Action!="brightness-set"||request.BrightnessPercent<0||request.BrightnessPercent>100)throw new ArgumentException("Informe um brilho entre 0 e 100%.");
   string before=backend.Fingerprint();uint value=(uint)Math.Round(report.BrightnessMaximum*request.BrightnessPercent/100.0);brightness.SendBrightness(value);
   report.TopologyUnchanged=before==backend.Fingerprint();report.Success=report.TopologyUnchanged;report.BrightnessPercent=(int)Math.Round(value*100.0/report.BrightnessMaximum);
   report.Message=report.Success?"Brilho enviado somente à tela selecionada: "+report.BrightnessPercent+"%.":"A conexão mudou durante o ajuste de brilho. Atualize o inventário antes de repetir.";return report;
  }
 }
 public static class PowerPolicy {
  public static List<uint> Values(string capabilities) {
   var m=Regex.Match(capabilities??"",@"\bD6\s*\(([^)]*)\)",RegexOptions.IgnoreCase);
   if(!m.Success)return new List<uint>();
   return Regex.Matches(m.Groups[1].Value,@"\b[0-9A-Fa-f]{2}\b").Cast<Match>().Select(v=>UInt32.Parse(v.Value,NumberStyles.HexNumber,CultureInfo.InvariantCulture)).Distinct().ToList();
  }
  public static uint OffValue(List<uint> values) {return !values.Contains(1)?0:values.Contains(5)?5u:values.Contains(4)?4u:values.Contains(3)?3u:values.Contains(2)?2u:0;}
  public static PowerReport Execute(PowerRequest request,IPowerBackend backend) {
   if(request.Action=="brightness-probe"||request.Action=="brightness-set")return BrightnessPolicy.Execute(request,backend);
   if(!Native.Same(request.Key,backend.Key))throw new InvalidOperationException("Identidade da tela mudou. Nenhum comando foi enviado.");
   if(request.Action=="probe")return backend.Probe();
   if(request.Action!="sleep"&&request.Action!="wake")throw new ArgumentException("Ação de energia inválida.");
   var report=request.Action=="sleep"?backend.Probe():new PowerReport{Key=request.Key,Supported=true};
   if(request.Action=="sleep"&&!report.Supported) {report.Success=false;return report;}
   string before=backend.Fingerprint();
   backend.Send(request.Action=="wake"?1:report.OffValue);
   string after=backend.Fingerprint();
   report.TopologyUnchanged=before==after;
   if(!report.TopologyUnchanged) {
    try {backend.Send(1);}catch(Exception){}
    report.Success=false;report.Supported=false;report.Message="O monitor alterou a conexão ao receber o comando. Foi tentado acendê-lo novamente. Esse método não atende a esta tela nesta conexão.";return report;
   }
   report.Success=true;report.RequestedOff=request.Action=="sleep";
   report.Message=request.Action=="sleep"?"Comando de apagar enviado somente a esta tela. O desktop permaneceu igual; confirme o painel visualmente.":"Comando de acender enviado à tela selecionada. O desktop permaneceu igual.";
   return report;
  }
 }
 public sealed class NativePower:IPowerBackend,IBrightnessBackend {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Physical {public IntPtr Handle;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;}
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,out uint count);
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor,uint count,[Out]Physical[] physical);
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool DestroyPhysicalMonitors(uint count,Physical[] physical);
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetCapabilitiesStringLength(IntPtr monitor,out uint length);
  [DllImport("dxva2.dll",SetLastError=true,CharSet=CharSet.Ansi)]static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor,StringBuilder value,uint length);
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor,byte code,out uint type,out uint current,out uint maximum);
  [DllImport("dxva2.dll",SetLastError=true)]static extern bool SetVCPFeature(IntPtr monitor,byte code,uint value);
  Physical[] handles;
  string sourceName;
  public string Key {get;private set;}
  public NativePower(string key) {
   var topology=Native.Capture(false);var targets=topology.Active.Where(d=>Native.Same(d.Key,key)).ToList();
   if(targets.Count!=1)throw new InvalidOperationException("Selecione uma tela ativa, identificada sem ambiguidades.");
   var desktop=Native.Monitors();Tuple<IntPtr,Native.MonitorInfo> monitor;if(!desktop.TryGetValue(targets[0].DeviceName,out monitor))throw new InvalidOperationException("Tela não encontrada no desktop.");
   if(topology.Active.Count(d=>Native.Same(d.DeviceName,targets[0].DeviceName))!=1)throw new InvalidOperationException("Controle individual indisponível em uma saída duplicada.");
   uint count;if(!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Item1,out count))throw new Win32Exception(Marshal.GetLastWin32Error(),"O driver não disponibilizou comunicação DDC/CI.");
   if(count!=1)throw new InvalidOperationException("A saída não corresponde a um único painel físico. Nenhum comando será enviado.");
   handles=new Physical[count];if(!GetPhysicalMonitorsFromHMONITOR(monitor.Item1,count,handles)){handles=null;throw new Win32Exception(Marshal.GetLastWin32Error());}
   Key=key;sourceName=targets[0].DeviceName;
  }
  public PowerReport Probe() {
   var report=new PowerReport{Key=Key};uint length,type,current,maximum;
   bool readable=GetVCPFeatureAndVCPFeatureReply(handles[0].Handle,0xd6,out type,out current,out maximum);report.CurrentValue=current;
   if(GetCapabilitiesStringLength(handles[0].Handle,out length)&&length>0&&length<65536) {
    var buffer=new StringBuilder((int)length);if(CapabilitiesRequestAndCapabilitiesReply(handles[0].Handle,buffer,length))report.Capabilities=buffer.ToString();
   }
   report.Values=PowerPolicy.Values(report.Capabilities);report.OffValue=PowerPolicy.OffValue(report.Values);report.Supported=report.OffValue!=0;report.Success=true;
   report.Message=report.Supported?"O monitor anuncia controle individual. O efeito de apagar e acender ainda precisa ser verificado neste painel.":"Controle individual de energia não confirmado nesta conexão. Verifique DDC/CI no menu do monitor, cabo, adaptadores e driver. Repouso por inatividade é um recurso diferente.";
   if(!readable&&!report.Supported)report.Message="O monitor não respondeu ao controle DDC/CI nesta conexão. Nenhum comando de energia foi enviado.";
   return report;
  }
  public void Send(uint value) {
   // No monitor-wide broadcast, black overlay or display-disable fallback exists here.
   var live=Native.Capture(false);if(!live.Active.Any(d=>Native.Same(d.Key,Key)&&Native.Same(d.DeviceName,sourceName)))throw new InvalidOperationException("A tela ou o caminho de conexão mudou. Atualize antes de enviar o comando.");
   if(!SetVCPFeature(handles[0].Handle,0xd6,value))throw new Win32Exception(Marshal.GetLastWin32Error(),"O painel não aceitou o comando DDC/CI.");
   // Let the firmware settle before checking desktop topology. Do not query DDC
   // capabilities or the power value after sleep: some panels wake on DDC reads.
   Thread.Sleep(value==1?600:2200);
  }
  public PowerReport ReadBrightness(){
   uint type,current,maximum;bool ok=GetVCPFeatureAndVCPFeatureReply(handles[0].Handle,0x10,out type,out current,out maximum);
   bool valid=ok&&maximum>0&&maximum<=65535&&current<=maximum;
   return new PowerReport{Key=Key,Success=valid,Supported=valid,BrightnessMaximum=valid?maximum:0,BrightnessPercent=valid?(int)Math.Round(current*100.0/maximum):0,Message=valid?"Brilho individual disponível para esta tela.":"A conexão não disponibilizou brilho individual por DDC/CI. Verifique o menu físico e o cabo do monitor."};
  }
  public void SendBrightness(uint value){var live=Native.Capture(false);if(!live.Active.Any(d=>Native.Same(d.Key,Key)&&Native.Same(d.DeviceName,sourceName)))throw new InvalidOperationException("A conexão da tela mudou.");if(!SetVCPFeature(handles[0].Handle,0x10,value))throw new Win32Exception(Marshal.GetLastWin32Error(),"O monitor não aceitou o ajuste de brilho.");}
  public string Fingerprint() {return LayoutPlanner.Fingerprint(Native.Capture(false).Active);}
  public void Dispose() {if(handles!=null){DestroyPhysicalMonitors((uint)handles.Length,handles);handles=null;}}
 }
 public sealed class PowerClient {
  readonly string directory;readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
  public PowerClient(string dataDirectory){directory=Path.Combine(dataDirectory,"power");}
  public async Task<PowerReport> Execute(string key,string action,int brightness=-1) {
   await gate.WaitAsync();string requestPath=null;
   try {
    Directory.CreateDirectory(directory);requestPath=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");Json.Write(requestPath,new PowerRequest{Key=key,Action=action,BrightnessPercent=brightness});
    using(var child=Process.Start(new ProcessStartInfo{FileName=Process.GetCurrentProcess().MainModule.FileName,Arguments="--power-worker "+Program.Quote(requestPath),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
     bool exited=await Task.Run(()=>child.WaitForExit(15000));
     if(!exited){try{child.Kill();child.WaitForExit(2000);}catch(InvalidOperationException){}throw new TimeoutException("O driver não respondeu em 15 segundos. A interface continua disponível. Confira o painel antes de repetir um comando de energia.");}
     string output=requestPath+".result";if(!File.Exists(output))throw new InvalidOperationException("O processo de energia terminou sem resposta.");
     var result=Json.Read<PowerReport>(output);if(!Native.Same(result.Key,key))throw new InvalidOperationException("A resposta não corresponde à tela selecionada.");return result;
    }
   }finally {if(requestPath!=null){foreach(string path in new[]{requestPath,requestPath+".result",requestPath+".bak"})try{if(File.Exists(path))File.Delete(path);}catch(IOException){}}gate.Release();}
  }
 }
}
