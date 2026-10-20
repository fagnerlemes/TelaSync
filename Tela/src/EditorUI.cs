using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Tela {
 public sealed partial class DesktopApp {
  LayoutEditor layoutDraft;
  string dragKey;Point dragStart;int dragX,dragY;bool dragMoved;
  double mapScale,mapOx,mapOy,mapMinX,mapMinY;
  TrayController tray;bool forceExit,closingPrompt;
  void InitializeEditor(){
   Btn("EditLayoutButton").Click+=(s,e)=>{if(busy)return;blanking.HideAll();if(layoutDraft==null)layoutDraft=new LayoutEditor(service.State.Remembered,service.State.PrimaryKey);Render();};
   Btn("CancelLayoutButton").Click+=(s,e)=>CancelEditor();
   Btn("SetPositionButton").Click+=(s,e)=>{try{int x,y,n;if(!Int32.TryParse(Ctrl<TextBox>("XPositionInput").Text,out x)||!Int32.TryParse(Ctrl<TextBox>("YPositionInput").Text,out y)||!Int32.TryParse(Ctrl<TextBox>("NumberInput").Text,out n))throw new InvalidOperationException("Informe números inteiros para X, Y e número da tela.");layoutDraft.Move(selected,x,y);layoutDraft.Number(selected,n);layoutDraft.Snap(selected,0);Render();}catch(Exception ex){Error(ex);}};
   Btn("MakePrimaryButton").Click+=(s,e)=>{if(layoutDraft==null||!service.IsActive(selected))return;layoutDraft.MakePrimary(selected);Render();};
   Btn("RotateLeftButton").Click+=(s,e)=>RotateSelected(-1);
   Btn("RotateRightButton").Click+=(s,e)=>RotateSelected(1);
   Btn("ApplyLayoutButton").Click+=async(s,e)=>{if(layoutDraft!=null){var draft=layoutDraft;await Change(()=>service.PrepareArrangement(draft));}};
   var map=Ctrl<Canvas>("MonitorMap");map.Focusable=true;
   map.PreviewMouseMove+=(s,e)=>{
    if(dragKey==null||layoutDraft==null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(map);if((p-dragStart).Length>3)dragMoved=true;if(!dragMoved)return;
    layoutDraft.Move(dragKey,dragX+(int)Math.Round((p.X-dragStart.X)/mapScale),dragY+(int)Math.Round((p.Y-dragStart.Y)/mapScale));RenderMap();RenderDetails();e.Handled=true;
   };
   map.PreviewMouseLeftButtonUp+=(s,e)=>{if(dragKey==null)return;var key=dragKey;bool moved=dragMoved;dragKey=null;Mouse.Capture(null);if(moved&&layoutDraft!=null)layoutDraft.Snap(key,Math.Min(100,(int)Math.Round(7/mapScale)));Render();e.Handled=true;};
   map.LostMouseCapture+=(s,e)=>{if(dragKey!=null){dragKey=null;Render();}};
   map.PreviewKeyDown+=(s,e)=>{
    if(layoutDraft==null)return;if(e.Key==Key.Escape){CancelEditor();e.Handled=true;return;}int dx=0,dy=0,step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?1:(Keyboard.Modifiers&ModifierKeys.Control)!=0?100:10;
    if(e.Key==Key.Left)dx=-step;else if(e.Key==Key.Right)dx=step;else if(e.Key==Key.Up)dy=-step;else if(e.Key==Key.Down)dy=step;else return;var d=layoutDraft.Find(selected);layoutDraft.Move(selected,d.X+dx,d.Y+dy);RenderMap();RenderDetails();e.Handled=true;
   };
  }
  void RotateSelected(int direction){if(busy||layoutDraft==null)return;try{layoutDraft.Rotate(selected,direction);Render();}catch(Exception ex){Error(ex);}}
  void CancelEditor(){dragKey=null;Mouse.Capture(null);layoutDraft=null;Render();}
  void StartDrag(Button button,MouseButtonEventArgs e){
   if(layoutDraft==null||busy)return;selected=(string)button.Tag;var d=layoutDraft.Find(selected);dragStart=e.GetPosition(Ctrl<Canvas>("MonitorMap"));dragX=d.X;dragY=d.Y;dragKey=d.Key;dragMoved=false;
   Ctrl<Canvas>("MonitorMap").Focus();Mouse.Capture(Ctrl<Canvas>("MonitorMap"));RenderDetails();RenderRows();RenderMap();e.Handled=true;
  }
  void RenderEditor(){
   bool edit=layoutDraft!=null;Ctrl<StackPanel>("LayoutControls").Visibility=edit?Visibility.Visible:Visibility.Collapsed;Btn("EditLayoutButton").Visibility=edit?Visibility.Collapsed:Visibility.Visible;
   Ctrl<TextBlock>("MapTitle").Text=edit?"Organizar telas":"Disposição das telas";Ctrl<TextBlock>("MapHint").Text=edit?"Arraste, ajuste as coordenadas ou gire a tela selecionada. Setas: 10 px · Shift: 1 px · Ctrl: 100 px. A mudança só vale depois de Aplicar.":"Organize posição e orientação das telas. A disposição fica salva para a próxima ativação.";
   Ctrl<Canvas>("MonitorMap").Cursor=edit?Cursors.Hand:Cursors.Arrow;
   if(edit){var d=layoutDraft.Find(selected);Ctrl<TextBox>("XPositionInput").Text=d.X.ToString();Ctrl<TextBox>("YPositionInput").Text=d.Y.ToString();Ctrl<TextBox>("NumberInput").Text=d.Number.ToString();Btn("MakePrimaryButton").IsEnabled=!busy&&service.IsActive(selected)&&!Native.Same(selected,layoutDraft.PrimaryKey);}
  }
  void InitializePlatform(){
   Window.WindowStyle=WindowStyle.None;WindowChrome.SetWindowChrome(Window,new WindowChrome{CaptionHeight=56,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});
   foreach(string name in new[]{"ThemeButton","WindowMinimizeButton","WindowMaximizeButton","WindowCloseButton"})WindowChrome.SetIsHitTestVisibleInChrome(Btn(name),true);
   Btn("WindowMinimizeButton").Click+=(s,e)=>Window.WindowState=WindowState.Minimized;
   Btn("WindowMaximizeButton").Click+=(s,e)=>Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
   Btn("WindowCloseButton").Click+=(s,e)=>Window.Close();
   Window.StateChanged+=(s,e)=>{Btn("WindowMaximizeButton").Content=Window.WindowState==WindowState.Maximized?"\uE923":"\uE922";};
   Btn("SettingsNav").Click+=(s,e)=>ShowPage("Preferências");
   Ctrl<CheckBox>("StartupCheck").Checked+=(s,e)=>Startup(true);Ctrl<CheckBox>("StartupCheck").Unchecked+=(s,e)=>Startup(false);
   Ctrl<CheckBox>("AskCloseCheck").Checked+=(s,e)=>SetAskBeforeClose(true);Ctrl<CheckBox>("AskCloseCheck").Unchecked+=(s,e)=>SetAskBeforeClose(false);
   RefreshStartup();RefreshClosePreference();
   Window.Closing+=(s,e)=>{
    if(preview||forceExit)return;e.Cancel=true;if(closingPrompt)return;
    if(busy){Say("Aguarde a operação atual antes de fechar o programa.");return;}
    closingPrompt=true;try{
     var choice=ClosePolicy.Action(service.State);
     if(choice==CloseChoice.Cancel){var dialog=new CloseChoiceWindow(Window,service.State.DarkTheme);dialog.ShowDialog();choice=dialog.Choice;if(dialog.RememberChoice&&choice!=CloseChoice.Cancel){ClosePolicy.Remember(service.State,choice,true);service.Store.Save(service.State);RefreshClosePreference();}}
     if(choice==CloseChoice.Tray)Window.Hide();else if(choice==CloseChoice.Exit){forceExit=true;e.Cancel=false;}
    }catch(Exception ex){Error(ex);}finally{closingPrompt=false;}
   };
  }
  void RefreshClosePreference(){
   updating=true;try{Ctrl<CheckBox>("AskCloseCheck").IsChecked=service.State.AskBeforeClose;Ctrl<TextBlock>("CloseBehaviorStatus").Text=service.State.AskBeforeClose?"O X perguntará se deseja minimizar para a bandeja ou fechar o programa.":ClosePolicy.Action(service.State)==CloseChoice.Tray?"O X minimizará para a bandeja. Para encerrar, use Sair no menu do ícone.":"O X encerrará o programa. Marque esta opção para voltar a perguntar.";}finally{updating=false;}
  }
  void SetAskBeforeClose(bool value){if(updating)return;service.State.AskBeforeClose=value;service.Store.Save(service.State);RefreshClosePreference();}
  void RefreshStartup(){
   try{updating=true;string current=new WindowsStartupStore().Read();bool owned=StartupPolicy.IsOwned(current);Ctrl<CheckBox>("StartupCheck").IsChecked=owned;Ctrl<TextBlock>("StartupStatus").Text=owned?"Inicialização automática ativada para este usuário.":"Inicialização automática desativada.";}catch(Exception ex){Ctrl<TextBlock>("StartupStatus").Text="Não foi possível consultar a inicialização: "+ex.Message;}finally{updating=false;}
  }
  void Startup(bool enabled){if(updating||preview)return;try{StartupPolicy.Set(new WindowsStartupStore(),Assembly.GetExecutingAssembly().Location,enabled);RefreshStartup();Say(enabled?"O TelaSync iniciará na bandeja com o Windows.":"Inicialização automática desativada.");}catch(Exception ex){RefreshStartup();Error(ex);}}
  void ShowMainWindow(){Window.Show();if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;Window.Activate();Native.SetForegroundWindow(new WindowInteropHelper(Window).Handle);}
  void ExitFromTray(){if(busy){ShowMainWindow();Say("Aguarde a operação atual antes de sair.");return;}forceExit=true;Window.Close();}
  public void AllowExit(){forceExit=true;}
  public void StartInTray(){if(tray!=null)Window.Hide();}
 }
}
