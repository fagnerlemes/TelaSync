using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Tela {
 public sealed partial class DesktopApp {
  readonly Dictionary<string,PowerReport> brightnessReports=new Dictionary<string,PowerReport>(StringComparer.OrdinalIgnoreCase);
  void InitializeBrightness(){
   Btn("ReadBrightnessButton").Click+=async(s,e)=>await Brightness(false);
   Btn("ApplyBrightnessButton").Click+=async(s,e)=>await Brightness(true);
   Ctrl<Slider>("BrightnessSlider").ValueChanged+=(s,e)=>Ctrl<TextBlock>("BrightnessValue").Text=Math.Round(e.NewValue)+"%";
  }
  async Task Brightness(bool set){string key=selected;int percent=(int)Math.Round(Ctrl<Slider>("BrightnessSlider").Value);await Run(async()=>{Say(set?"Ajustando o brilho da tela selecionada…":"Consultando o brilho individual…");var report=await power.Execute(key,set?"brightness-set":"brightness-probe",percent);brightnessReports[key]=report;Say(report.Message);});}
  void RenderBrightness(){
   PowerReport report;bool active=service.IsActive(selected),known=brightnessReports.TryGetValue(selected,out report),awake=!service.State.PowerRequestedOff.Contains(selected);
   bool available=active&&known&&report.Supported&&awake;
   Ctrl<Slider>("BrightnessSlider").IsEnabled=!busy&&available;Btn("ReadBrightnessButton").IsEnabled=!busy&&active&&awake;Btn("ApplyBrightnessButton").IsEnabled=!busy&&available;
   if(known&&report.Supported){if(!busy)Ctrl<Slider>("BrightnessSlider").Value=report.BrightnessPercent;Ctrl<TextBlock>("BrightnessValue").Text=Math.Round(Ctrl<Slider>("BrightnessSlider").Value)+"%";}else Ctrl<TextBlock>("BrightnessValue").Text="—";
   Ctrl<TextBlock>("BrightnessHint").Text=!awake?"Acenda o painel antes de consultar o brilho.":known&&!report.Supported?"Brilho individual indisponível nesta conexão.":known?"Ajuste o controle e clique em Aplicar.":"Consulte o brilho para liberar o controle.";
  }
 }
}
