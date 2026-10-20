using System;
using System.Linq;
using System.Windows.Controls;

namespace Tela {
 public sealed partial class DesktopApp {
  readonly BlankingController blanking=new BlankingController();
  void InitializeBlanking(){
   blanking.Changed+=()=>Render();
   Btn("BlackScreenButton").Click+=(s,e)=>{if(busy||layoutDraft!=null||preview)return;try{
    if(blanking.Hide(selected)){Say("Tela preta removida da tela selecionada.");return;}
    var live=Native.Capture(false);var target=live.Active.FirstOrDefault(d=>Native.Same(d.Key,selected));if(target==null)throw new InvalidOperationException("Selecione uma tela ativa para usar Tela preta.");
    if(live.Active.Count(d=>Native.Same(d.DeviceName,target.DeviceName))!=1)throw new InvalidOperationException("Tela preta individual exige uma saída estendida, sem duplicação.");
    Tuple<IntPtr,Native.MonitorInfo> monitor;if(!Native.Monitors().TryGetValue(target.DeviceName,out monitor))throw new InvalidOperationException("A área desta tela mudou. Atualize antes de tentar novamente.");
    MoveAwayFrom(selected);blanking.Show(selected,monitor.Item2.Bounds);Say("Tela preta somente na tela selecionada. A iluminação continua ligada. Use Acender no aplicativo ou Ctrl+Alt+L para voltar; mouse e teclado não removem a tela preta.");
   }catch(Exception ex){Error(ex);}};
  }
 }
}
