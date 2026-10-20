using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace Tela {
 public static class ThemeResources {
  public static void Load(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Tela.Theme.xaml"))Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));}
  public static void Set(bool dark){
   string[] keys={"AppBackground","Panel","Sidebar","Ink","Muted","Line","AccentInk","Soft","Screen"};
   string[] colors=dark?new[]{"#111A2E","#1C2944","#151F35","#F2F6FF","#B6C4E3","#61739A","#65E5EA","#39305C","#2B3D61"}:new[]{"#EEF2FA","#FFFFFF","#E0E7F4","#17243B","#455575","#8293AF","#006A7C","#E8DFFA","#D6E0F1"};
   for(int i=0;i<keys.Length;i++)Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
   Application.Current.Resources["Accent"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#00BCCB":"#00718D"));
   Application.Current.Resources["SelectionAccent"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#A684FF":"#7040C6"));
   Application.Current.Resources["PrimaryInk"]=new SolidColorBrush(dark?Color.FromRgb(5,29,49):Colors.White);
   Application.Current.Resources["PanelFill"]=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(dark?"#253452":"#FFFFFF"),(Color)ColorConverter.ConvertFromString(dark?"#19253E":"#F8FAFF"),45);
   Application.Current.Resources["PrimaryFill"]=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(dark?"#00B9DB":"#00718D"),(Color)ColorConverter.ConvertFromString(dark?"#00D0AD":"#007C6A"),0);
  }
 }
 public static class NativeTheme {
  [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
  [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
  public static readonly uint ShowMessage=RegisterWindowMessage("TelaSync.ShowMainWindow");
  public static void RequestShow(){PostMessage(new IntPtr(0xffff),ShowMessage,IntPtr.Zero,IntPtr.Zero);}
  static int ColorValue(Color c){return c.R|(c.G<<8)|(c.B<<16);}
  public static int Apply(Window window,bool dark){
   var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return 0;
   int value=dark?1:0;int code=DwmSetWindowAttribute(handle,20,ref value,4);if(code!=0)code=DwmSetWindowAttribute(handle,19,ref value,4);
   int caption=ColorValue(((SolidColorBrush)Application.Current.FindResource("Panel")).Color),ink=ColorValue(((SolidColorBrush)Application.Current.FindResource("Ink")).Color);
   DwmSetWindowAttribute(handle,35,ref caption,4);DwmSetWindowAttribute(handle,36,ref ink,4);return code;
  }
  public static int ReadDark(Window window){int value;return DwmGetWindowAttribute(new WindowInteropHelper(window).Handle,20,out value,4)==0?value:-1;}
  public static int ReadCaption(Window window){int value;return DwmGetWindowAttribute(new WindowInteropHelper(window).Handle,35,out value,4)==0?value:-1;}
 }
 public interface IStartupStore {string Read();void Write(string value);void Delete();}
 public static class StartupPolicy {
  public static string Command(string executable){return Program.Quote(Path.GetFullPath(executable))+" --start-minimized";}
  public static bool IsOwned(string value){
   if(String.IsNullOrWhiteSpace(value)||value[0]!='"')return false;int end=value.IndexOf('"',1);if(end<2)return false;
   return Native.Same(Path.GetFileName(value.Substring(1,end-1)),ProductInfo.Executable);
  }
  public static void Set(IStartupStore store,string executable,bool enabled){string prior=store.Read();if(prior!=null&&!IsOwned(prior))throw new InvalidOperationException("Já existe outra configuração com esse nome na inicialização. Ela foi preservada.");if(enabled)store.Write(Command(executable));else if(prior!=null)store.Delete();}
 }
 public sealed class WindowsStartupStore:IStartupStore {
  const string PathName=@"Software\Microsoft\Windows\CurrentVersion\Run";
  public string Read(){using(var key=Registry.CurrentUser.OpenSubKey(PathName))return key==null?null:key.GetValue(ProductInfo.AppId) as string;}
  public void Write(string value){using(var key=Registry.CurrentUser.CreateSubKey(PathName))key.SetValue(ProductInfo.AppId,value,RegistryValueKind.String);}
  public void Delete(){using(var key=Registry.CurrentUser.OpenSubKey(PathName,true))if(key!=null)key.DeleteValue(ProductInfo.AppId,false);}
 }
 public sealed class TrayController:IDisposable {
  readonly Forms.NotifyIcon icon;readonly Forms.ContextMenuStrip menu;readonly Drawing.Icon image;
  public TrayController(Action open,Action exit,bool visible){
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Tela.Icon.ico"))using(var resource=new Drawing.Icon(stream))image=(Drawing.Icon)resource.Clone();
   menu=new Forms.ContextMenuStrip{ShowImageMargin=false};menu.Items.Add("Abrir "+ProductInfo.Name,null,(s,e)=>open());menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Sair",null,(s,e)=>exit());
   icon=new Forms.NotifyIcon{Text=ProductInfo.Name+" "+ProductInfo.Version,Icon=image,ContextMenuStrip=menu,Visible=visible};icon.DoubleClick+=(s,e)=>open();
  }
  public void Theme(bool dark){var bg=dark?Drawing.Color.FromArgb(28,41,68):Drawing.Color.White;var fg=dark?Drawing.Color.FromArgb(242,246,255):Drawing.Color.FromArgb(23,36,59);menu.Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors(dark));menu.BackColor=bg;menu.ForeColor=fg;foreach(Forms.ToolStripItem item in menu.Items){item.BackColor=bg;item.ForeColor=fg;}}
  public bool Visible {get{return icon.Visible;}}
  public int MenuItems {get{return menu.Items.Count;}}
  public void Dispose(){icon.Visible=false;icon.Dispose();menu.Dispose();image.Dispose();}
 }
 sealed class TrayColors:Forms.ProfessionalColorTable {
  readonly bool dark;public TrayColors(bool value){dark=value;UseSystemColors=false;}
  Drawing.Color Bg {get{return dark?Drawing.Color.FromArgb(28,41,68):Drawing.Color.White;}}
  public override Drawing.Color ToolStripDropDownBackground {get{return Bg;}}
  public override Drawing.Color ImageMarginGradientBegin {get{return Bg;}}public override Drawing.Color ImageMarginGradientMiddle {get{return Bg;}}public override Drawing.Color ImageMarginGradientEnd {get{return Bg;}}
  public override Drawing.Color MenuItemSelected {get{return dark?Drawing.Color.FromArgb(57,48,92):Drawing.Color.FromArgb(232,223,250);}}
  public override Drawing.Color MenuItemBorder {get{return dark?Drawing.Color.FromArgb(166,132,255):Drawing.Color.FromArgb(112,64,198);}}
  public override Drawing.Color MenuBorder {get{return dark?Drawing.Color.FromArgb(74,94,132):Drawing.Color.FromArgb(175,188,213);}}
 }
 public enum CloseChoice {Cancel,Tray,Exit}
 public sealed class CloseChoiceWindow:Window {
  public CloseChoice Choice=CloseChoice.Cancel;
  public readonly CheckBox RememberCheck=new CheckBox{Content="Memorizar minha escolha",Margin=new Thickness(0,0,0,20)};
  public bool RememberChoice {get{return RememberCheck.IsChecked==true;}}
  public CloseChoiceWindow(Window owner,bool dark){
   Owner=owner;Title="Fechar "+ProductInfo.Name;Width=600;Height=300;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;FontFamily=new FontFamily("Segoe UI");Background=(Brush)Application.Current.FindResource("Panel");Foreground=(Brush)Application.Current.FindResource("Ink");
   var panel=new StackPanel{Margin=new Thickness(26)};panel.Children.Add(new TextBlock{Text="Como deseja fechar a janela?",FontSize=21,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock{Text="Na bandeja, o TelaSync continua disponível pelo ícone ao lado do relógio. Você pode voltar a perguntar em Preferências.",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Application.Current.FindResource("Muted"),Margin=new Thickness(0,12,0,18)});panel.Children.Add(RememberCheck);
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
   var cancel=new Button{Content="Cancelar",IsCancel=true};var exit=new Button{Content="Fechar programa",Margin=new Thickness(8,0,0,0)};var tray=new Button{Content="Minimizar para bandeja",IsDefault=true,Margin=new Thickness(8,0,0,0),Style=(Style)Application.Current.FindResource("PrimaryButton")};
   cancel.Click+=(s,e)=>{Choice=CloseChoice.Cancel;DialogResult=false;};exit.Click+=(s,e)=>{Choice=CloseChoice.Exit;DialogResult=true;};tray.Click+=(s,e)=>{Choice=CloseChoice.Tray;DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(exit);buttons.Children.Add(tray);panel.Children.Add(buttons);Content=panel;SourceInitialized+=(s,e)=>NativeTheme.Apply(this,dark);
  }
 }
}
