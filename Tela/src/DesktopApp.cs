using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tela {
 public sealed partial class DesktopApp {
  public readonly Window Window;
  readonly MonitorService service;
  readonly PowerClient power;
  readonly Dictionary<string,PowerReport> powerReports=new Dictionary<string,PowerReport>(StringComparer.OrdinalIgnoreCase);
  string selected,page="Telas";
  bool busy,updating,preview;
  DispatcherTimer refreshTimer;
  HwndSource source;
  public DesktopApp(string dataDirectory,bool previewMode) {
   preview=previewMode;ThemeResources.Load();service=new MonitorService(dataDirectory);service.Refresh(true);power=new PowerClient(dataDirectory);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Tela.MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   Window.Title=ProductInfo.Name+" — Gerenciador de monitores";Window.Height=Math.Min(Window.Height,Math.Max(Window.MinHeight,SystemParameters.WorkArea.Height-32));Window.Width=Math.Min(Window.Width,Math.Max(Window.MinWidth,SystemParameters.WorkArea.Width-32));
   Ctrl<TextBlock>("BrandTitle").Text=ProductInfo.Name;Ctrl<TextBlock>("VersionLabel").Text="Versão "+ProductInfo.Version;
   var icon=ResourceImage("Tela.Icon.png");Window.Icon=icon;Ctrl<Image>("BrandIcon").Source=icon;
   Ctrl<System.Windows.Shapes.Rectangle>("AuthorSignature").OpacityMask=new ImageBrush(ResourceImage("Tela.Signature.png")){Stretch=Stretch.Uniform};
   Btn("AboutButton").Click+=(s,e)=>About();
   selected=service.State.Remembered.FirstOrDefault(d=>service.IsActive(d.Key)&&!d.IsPrimary)!=null?service.State.Remembered.First(d=>service.IsActive(d.Key)&&!d.IsPrimary).Key:service.Current.Active[0].Key;
   Btn("TelasNav").Click+=(s,e)=>ShowPage("Telas");Btn("PerfisNav").Click+=(s,e)=>ShowPage("Perfis");Btn("AtalhosNav").Click+=(s,e)=>ShowPage("Atalhos");
   Btn("RefreshButton").Click+=async(s,e)=>await Run(async()=>{await Task.Run(()=>service.Refresh(true));blanking.Reconcile(service.Current.Active);powerReports.Clear();Render();Say("Inventário atualizado. A memória anterior foi preservada.");});
   Btn("RememberButton").Click+=(s,e)=>{if(busy)return;try{service.RememberCurrent();Render();Say("Disposição atual registrada. As telas desativadas mantêm a memória anterior.");}catch(Exception ex){Error(ex);}};
   Btn("ThemeButton").Click+=(s,e)=>{service.State.DarkTheme=!service.State.DarkTheme;service.Store.Save(service.State);Theme();Render();};
   Btn("ToggleButton").Click+=async(s,e)=>await Toggle();Btn("PowerButton").Click+=async(s,e)=>await Power("sleep");Btn("WakeButton").Click+=async(s,e)=>await Power("wake");Btn("ProbeButton").Click+=async(s,e)=>await Power("probe");
   Btn("SaveProfileButton").Click+=(s,e)=>{if(busy)return;try{service.SaveProfile(Ctrl<ComboBox>("ProfilePicker").Text);RenderProfiles();Say("Perfil salvo com as posições lembradas e as telas atualmente ativas.");}catch(Exception ex){Error(ex);}};
   Btn("ApplyProfileButton").Click+=async(s,e)=>{var p=service.State.Profiles.FirstOrDefault(x=>Native.Same(x.Name,Ctrl<ComboBox>("ProfilePicker").Text));if(p==null){Say("Escolha um perfil salvo para aplicar.");return;}await Change(()=>service.PrepareProfile(p));};
   Ctrl<CheckBox>("PreserveCheck").Checked+=(s,e)=>SetPreserve(true);Ctrl<CheckBox>("PreserveCheck").Unchecked+=(s,e)=>SetPreserve(false);
   Btn("IdentifyButton").Click+=(s,e)=>Identify();
   Btn("DiagnosticsButton").Click+=(s,e)=>{try{string file=Path.Combine(service.Store.DirectoryPath,"inventario.json");Json.Write(file,new{Captured=DateTimeOffset.Now.ToString("o"),Active=service.Current.Active,Remembered=service.State.Remembered,Power=powerReports.Values.ToList()});System.Diagnostics.Process.Start("explorer.exe",Program.Quote(service.Store.DirectoryPath));Say("Diagnóstico salvo na pasta de dados.");}catch(Exception ex){Error(ex);}};
   Ctrl<Canvas>("MonitorMap").SizeChanged+=(s,e)=>RenderMap();
   Window.SourceInitialized+=(s,e)=>Attach();Window.Closed+=(s,e)=>Detach();
   Window.Loaded+=(s,e)=>NativeTheme.Apply(Window,service.State.DarkTheme);Window.Activated+=(s,e)=>NativeTheme.Apply(Window,service.State.DarkTheme);
   InitializeEditor();InitializeBrightness();InitializePlatform();InitializeBlanking();InitializeTvMode();Theme();Render();
  }
  T Ctrl<T>(string name) where T:class {return (T)Window.FindName(name);}
  static BitmapSource ResourceImage(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);image.Freeze();return image;}}
  void About() {
   var dialog=new Window{Title="Sobre o "+ProductInfo.Name,Width=460,Height=390,ResizeMode=ResizeMode.NoResize,Owner=Window,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("Panel"),Foreground=B("Ink"),FontFamily=new FontFamily("Segoe UI"),Icon=Window.Icon};
   var content=new StackPanel{Margin=new Thickness(28)};content.Children.Add(new Image{Source=Window.Icon,Width=58,Height=58,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,16)});content.Children.Add(Text(ProductInfo.Name,25,"Ink"));content.Children.Add(Text("Versão "+ProductInfo.Version,13,"Muted"));
   content.Children.Add(new TextBlock{Text="Organização de telas com memória de posição e controle individual de energia.",Foreground=B("Muted"),TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,15,0,18)});
   content.Children.Add(new System.Windows.Shapes.Rectangle{Width=250,Height=60,HorizontalAlignment=HorizontalAlignment.Left,Fill=B("Ink"),OpacityMask=new ImageBrush(ResourceImage("Tela.Signature.png")){Stretch=Stretch.Uniform}});
   content.Children.Add(new TextBlock{Text=ProductInfo.Signature,Foreground=B("Muted"),FontSize=12,Margin=new Thickness(0,6,0,0)});dialog.Content=content;dialog.SourceInitialized+=(s,e)=>NativeTheme.Apply(dialog,service.State.DarkTheme);dialog.ShowDialog();
  }
  Button Btn(string name){return Ctrl<Button>(name);}
  Brush B(string key){return (Brush)Window.FindResource(key);}
  DisplayRecord Selected {get{return layoutDraft==null?service.State.Remembered.First(d=>Native.Same(d.Key,selected)):layoutDraft.Find(selected);}}
  void Say(string message){var feedback=Ctrl<TextBlock>("Feedback");feedback.Text=message;feedback.Visibility=Visibility.Visible;if(Window.IsLoaded)Window.Dispatcher.BeginInvoke(new Action(()=>feedback.BringIntoView()),DispatcherPriority.Background);service.Store.Log(message);}
  void Error(Exception e){Say(e.Message);service.Store.Log(e.ToString());}
  void SetPreserve(bool value){if(updating)return;service.State.PreservePositions=value;service.Store.Save(service.State);Ctrl<TextBlock>("MemoryStatus").Text="Memória de posição "+(value?"ativada":"desativada");}
  void Theme() {
   var dark=service.State.DarkTheme;ThemeResources.Set(dark);NativeTheme.Apply(Window,dark);if(tray!=null)tray.Theme(dark);
   Btn("ThemeButton").Content=dark?"Tema claro":"Tema escuro";
  }

  void ShowPage(string name){page=name;if(name=="Preferências")RefreshStartup();Render();}
  void Render() {
   if(!service.State.Remembered.Any(d=>Native.Same(d.Key,selected)))selected=service.State.Remembered.First().Key;
   Ctrl<TextBlock>("PageTitle").Text=page=="Telas"?"Monitores":page;
   int count=service.Current.Active.Count;Ctrl<TextBlock>("CountLabel").Text=count+" "+(count==1?"tela ativa":"telas ativas")+" · "+(service.State.Remembered.Count-count)+" fora do desktop";
   Ctrl<FrameworkElement>("MonitorsPage").Visibility=page=="Telas"?Visibility.Visible:Visibility.Collapsed;Ctrl<FrameworkElement>("ProfilesPage").Visibility=page=="Perfis"?Visibility.Visible:Visibility.Collapsed;Ctrl<FrameworkElement>("ShortcutsPage").Visibility=page=="Atalhos"?Visibility.Visible:Visibility.Collapsed;
   Ctrl<FrameworkElement>("SettingsPage").Visibility=page=="Preferências"?Visibility.Visible:Visibility.Collapsed;
   Btn("SettingsNav").Background=B(page=="Preferências"?"Soft":"Sidebar");
   Ctrl<FrameworkElement>("ProfileBar").Visibility=page=="Preferências"?Visibility.Collapsed:Visibility.Visible;
   foreach(string nav in new[]{"Telas","Perfis","Atalhos"}){Btn(nav+"Nav").Background=B(page==nav?"Soft":"Sidebar");Btn(nav+"Nav").Foreground=B(page==nav?"AccentInk":"Ink");}
   updating=true;Ctrl<CheckBox>("PreserveCheck").IsChecked=service.State.PreservePositions;updating=false;
   Ctrl<TextBlock>("MemoryStatus").Text="Memória de posição "+(service.State.PreservePositions?"ativada":"desativada");
   RenderRows();RenderEditor();RenderMap();RenderDetails();RenderProfiles();Buttons();
  }
  TextBlock Text(string text,double size,string brush) {return new TextBlock{Text=text,FontSize=size,Foreground=B(brush),TextWrapping=TextWrapping.Wrap};}
  void RenderRows() {
   var rows=Ctrl<StackPanel>("DisplayRows");rows.Children.Clear();Ctrl<TextBlock>("DeviceCount").Text=service.State.Remembered.Count+" dispositivos lembrados";
   foreach(var d in (layoutDraft==null?service.State.Remembered:layoutDraft.Displays).OrderBy(d=>d.X).ThenBy(d=>d.Number)) {
    var button=new Button{HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(10,10,10,10),Margin=new Thickness(0,0,0,5),BorderThickness=new Thickness(0),Background=B(Native.Same(d.Key,selected)?"Soft":"AppBackground"),Tag=d.Key};
    AutomationProperties.SetName(button,"Selecionar tela "+d.Number+", "+d.Name);
    var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(33)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    var num=new Border{BorderBrush=B("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Width=26,Height=24,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=d.Number.ToString(),FontSize=12,Foreground=B("Ink"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
    var words=new StackPanel();words.Children.Add(Text(d.Name,14,"Ink"));words.Children.Add(Text(d.Width+" × "+d.Height+(d.InitialPosition?" · posição inicial":""),12,"Muted"));Grid.SetColumn(words,1);
    string status=service.IsActive(d.Key)?(blanking.Contains(d.Key)?"Tela preta":service.State.PowerRequestedOff.Contains(d.Key)?"Apagar solicitado":service.Current.Active.First(a=>Native.Same(a.Key,d.Key)).IsPrimary?"Principal":"Ativa"):service.IsAvailable(d.Key)?"Desativada":"Sem conexão";
    var label=Text(status,12,"Muted");label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(10,0,0,0);label.MaxWidth=98;Grid.SetColumn(label,2);grid.Children.Add(num);grid.Children.Add(words);grid.Children.Add(label);button.Content=grid;button.Click+=(s,e)=>{selected=(string)((Button)s).Tag;Render();};rows.Children.Add(button);
   }
  }
  void RenderMap() {
   if(busy)return;
   var canvas=Ctrl<Canvas>("MonitorMap");canvas.Children.Clear();double width=canvas.ActualWidth;if(width<10)width=520;
   var displays=(layoutDraft==null?service.State.Remembered:layoutDraft.Displays).Where(d=>!d.InitialPosition||service.IsActive(d.Key)).ToList();if(displays.Count==0)return;
   double minX=displays.Min(d=>d.X),minY=displays.Min(d=>d.Y),maxX=displays.Max(d=>d.Right),maxY=displays.Max(d=>d.Bottom);
   double height=canvas.Height;
   if(dragKey==null){mapScale=Math.Min((width-32)/Math.Max(1,maxX-minX),(height-32)/Math.Max(1,maxY-minY));mapMinX=minX;mapMinY=minY;mapOx=(width-(maxX-minX)*mapScale)/2;mapOy=(height-(maxY-minY)*mapScale)/2;}
   double scale=mapScale,ox=mapOx,oy=mapOy;minX=mapMinX;minY=mapMinY;
   foreach(var d in displays) {
    bool active=service.IsActive(d.Key),chosen=Native.Same(d.Key,selected),visual=blanking.Contains(d.Key);bool off=service.State.PowerRequestedOff.Contains(d.Key)||visual;
    var button=new Button{Width=Math.Max(24,d.Width*scale-4),Height=Math.Max(30,d.Height*scale-4),Padding=new Thickness(2),Background=active?B(off?"Sidebar":chosen?"Soft":"Screen"):B("AppBackground"),BorderBrush=B(chosen?"SelectionAccent":active?"Line":"Muted"),BorderThickness=new Thickness(chosen?2:1),Tag=d.Key};
    var content=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var number=Text(d.Number.ToString(),28,chosen?"AccentInk":"Ink");number.HorizontalAlignment=HorizontalAlignment.Center;content.Children.Add(number);
    bool main=layoutDraft!=null?d.IsPrimary:active&&service.Current.Active.First(a=>Native.Same(a.Key,d.Key)).IsPrimary;
    if(button.Width>110){var caption=Text(!active?"Lugar preservado":visual?"Tela preta":off?"Apagar solicitado":main?"Principal":d.Width+" × "+d.Height,11,"Muted");caption.HorizontalAlignment=HorizontalAlignment.Center;caption.TextAlignment=TextAlignment.Center;content.Children.Add(caption);}
    button.Content=content;button.PreviewMouseLeftButtonDown+=(s,e)=>StartDrag((Button)s,e);AutomationProperties.SetName(button,"Tela "+d.Number+", "+d.Name+", "+(active?"ativa":"desativada, posição lembrada"));Canvas.SetLeft(button,ox+(d.X-minX)*scale+2);Canvas.SetTop(button,oy+(d.Y-minY)*scale+2);button.Click+=(s,e)=>{selected=(string)((Button)s).Tag;Render();};canvas.Children.Add(button);
   }
  }
  void RenderDetails() {
   var d=Selected;var live=service.Current.Active.FirstOrDefault(a=>Native.Same(a.Key,d.Key));
   Ctrl<TextBlock>("SelectedNumber").Text="Tela "+d.Number+" selecionada";Ctrl<TextBlock>("SelectedName").Text=d.Name;Ctrl<TextBlock>("SelectedModel").Text=d.DeviceName;
   bool available=service.IsAvailable(d.Key);Ctrl<TextBlock>("SelectedStatus").Text=live==null?available?"Desativada · disponível para reativar":"Sem conexão · ligue pelo botão físico ou confira o cabo":blanking.Contains(d.Key)?"Tela preta · iluminação ligada":service.State.PowerRequestedOff.Contains(d.Key)?"Apagar solicitado · confira o painel":"Ativa · conectada ao desktop";
   Btn("BlackScreenButton").Content=blanking.Contains(d.Key)?"Remover tela preta":"Tela preta";
   Ctrl<TextBlock>("ResolutionValue").Text=d.Width+" × "+d.Height;Ctrl<TextBlock>("PositionValue").Text=d.InitialPosition?"A registrar":d.X+", "+d.Y;
   uint rotation=Native.U32(Convert.FromBase64String(d.PathData),40);Ctrl<TextBlock>("OrientationValue").Text=(d.Height>d.Width?"Vertical":"Horizontal")+" · "+((rotation-1)*90)+"°";Ctrl<TextBlock>("CurrentPositionValue").Text=live==null?available?"Desativada":"Sem conexão":live.X+", "+live.Y;
   Ctrl<TextBlock>("MemoryNote").Text=d.InitialPosition?"A posição será registrada na primeira ativação.":"Volta à posição salva ao reativar.";
   Btn("ToggleButton").Content=live==null?available?d.InitialPosition?"Ativar tela":"Reativar tela":"Sem conexão":"Desativar tela";Ctrl<TextBlock>("ToggleNote").Text=live==null?available?"Reativa a tela na posição salva.":"Após ligar ou reconectar o monitor, clique em Atualizar.":"Desativar remove só esta tela do desktop.";
   PowerReport report;var support=Ctrl<TextBlock>("PowerSupportLabel");support.Text=PowerPolicy.SleepIssue(d.Key)??(powerReports.TryGetValue(d.Key,out report)?report.Message:"Compatibilidade individual ainda não verificada.");support.ToolTip=support.Text;RenderEditor();RenderBrightness();
  }
  void RenderProfiles() {
   var picker=Ctrl<ComboBox>("ProfilePicker");string old=picker.Text;picker.Items.Clear();foreach(var profile in service.State.Profiles)picker.Items.Add(profile.Name);picker.Text=String.IsNullOrWhiteSpace(old)?service.State.Profiles.First().Name:old;
   var rows=Ctrl<StackPanel>("ProfileRows");rows.Children.Clear();foreach(var profile in service.State.Profiles) {
    var grid=new Grid{Margin=new Thickness(0,0,0,15)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=Text(profile.Name+"\n"+profile.Enabled.Count+" telas no perfil",14,"Ink");var apply=new Button{Content="Aplicar perfil",Tag=profile,VerticalAlignment=VerticalAlignment.Center};apply.Click+=async(s,e)=>await Change(()=>service.PrepareProfile((NamedProfile)((Button)s).Tag));Grid.SetColumn(apply,1);grid.Children.Add(label);grid.Children.Add(apply);rows.Children.Add(grid);
   }
  }
  void Buttons() {
   bool active=service.IsActive(selected);PowerReport r;bool supported=powerReports.TryGetValue(selected,out r)&&r.Supported;
   foreach(string name in new[]{"ToggleButton","ProbeButton","WakeButton","PowerButton","RememberButton","ApplyProfileButton","SaveProfileButton","RefreshButton","ThemeButton","EditLayoutButton","ApplyLayoutButton","CancelLayoutButton","SetPositionButton"})Btn(name).IsEnabled=!busy;
   Btn("PowerButton").IsEnabled=!busy&&active&&supported&&PowerPolicy.SleepIssue(selected)==null&&!blanking.Contains(selected);Btn("WakeButton").IsEnabled=!busy&&(active||blanking.Contains(selected));Btn("ProbeButton").IsEnabled=!busy&&active;
   Btn("BlackScreenButton").IsEnabled=!busy&&active&&layoutDraft==null&&!service.State.PowerRequestedOff.Contains(selected);
   Btn("ToggleButton").IsEnabled=!busy&&(active?service.Current.Active.Count>1:service.IsAvailable(selected));Ctrl<CheckBox>("PreserveCheck").IsEnabled=!busy;Ctrl<TextBlock>("OperationStatus").Text=busy?"Operação em andamento…":"Aguardando uma ação";
   if(layoutDraft!=null){foreach(string name in new[]{"ToggleButton","RememberButton","ApplyProfileButton","SaveProfileButton","RefreshButton","PowerButton","WakeButton","ProbeButton"})Btn(name).IsEnabled=false;}
   RenderBrightness();Ctrl<StackPanel>("LayoutControls").IsEnabled=!busy;
   Ctrl<StackPanel>("DisplayRows").IsEnabled=!busy;Ctrl<Canvas>("MonitorMap").IsEnabled=!busy;Ctrl<StackPanel>("ProfileRows").IsEnabled=!busy;
   RenderTvMode();
  }
  async Task Run(Func<Task> action) {if(busy)return;busy=true;Buttons();try{await action();}catch(Exception e){Error(e);}finally{busy=false;Render();}}
  async Task Toggle(){string key=selected;await Change(()=>service.PrepareToggle(key));}
  async Task Change(Func<PreparedChange> prepare,string successMessage=null) {
   await Run(async()=>{
    blanking.HideAll();
    Say("Validando a configuração no Windows…");PreparedChange change=await Task.Run(prepare);RecoveryGuard guard=null;bool accepted=false;
    Exception failure=null;
    try {
     guard=await Task.Run(()=>new RecoveryGuard(service.Store.DirectoryPath,change.Before.Image()));
     Say("Aplicando layout temporário…");Native.Check(await Task.Run(()=>Native.Configure(change.Proposed,false,false)),"Não foi possível aplicar o layout");
     var actual=await Task.Run(()=>Native.Capture(false));if(LayoutPlanner.Fingerprint(actual.Active)!=LayoutPlanner.Fingerprint(change.Desired))throw new InvalidOperationException("O driver não manteve as posições solicitadas. A configuração anterior será restaurada.");
     KeepWindowVisible(actual.Active);var confirm=new ConfirmationWindow();confirm.Owner=Window;bool keep=confirm.ShowDialog()==true;
     if(keep){await Task.Run(()=>guard.Commit(()=>{Native.Check(Native.Configure(change.Proposed,false,true),"Não foi possível salvar o layout no Windows");service.Accept(change);}));accepted=true;bool edited=layoutDraft!=null;layoutDraft=null;Say(successMessage??(edited?"Nova disposição confirmada e salva na memória de posição.":"Layout confirmado. A memória original das posições foi preservada."));}
     else {await Task.Run(()=>guard.Revert());Say("A configuração anterior foi solicitada. Nenhuma nova posição foi registrada.");}
    }catch(Exception e){failure=e;}
    if(failure!=null&&guard!=null&&!accepted)try{await Task.Run(()=>guard.Revert());}catch(Exception e){service.Store.Log("Reversão falhou: "+e);failure=new InvalidOperationException(failure.Message+" A reversão também não foi confirmada pelo Windows. Confira a disposição nas Configurações de Tela.",failure);}
    try{await Task.Run(()=>service.Refresh(false));}catch(Exception e){if(failure==null)failure=e;else service.Store.Log(e.ToString());}
    powerReports.Clear();Render();if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
   });
  }
  async Task Power(string action) {
   string key=selected;
   if(action=="wake"&&blanking.Hide(key)){Say("Tela preta removida da tela selecionada. Nenhum comando de energia foi enviado.");return;}
   await Run(async()=>{
    if(action!="probe")MoveAwayFrom(key);
    Say(action=="probe"?"Consultando o suporte individual desta tela…":"Enviando comando somente ao painel selecionado…");
    var result=await power.Execute(key,action);powerReports[key]=result;Say(result.Message);
    if(result.Success&&action!="probe"){service.State.PowerRequestedOff.RemoveAll(k=>Native.Same(k,key));if(result.RequestedOff)service.State.PowerRequestedOff.Add(key);service.Store.Save(service.State);}
    await Task.Run(()=>service.Refresh(false));blanking.Reconcile(service.Current.Active);Render();
   });
  }
  void Attach() {
   if(preview)return;NativeTheme.Apply(Window,service.State.DarkTheme);tray=new TrayController(()=>Window.Dispatcher.BeginInvoke(new Action(ShowMainWindow)),()=>Window.Dispatcher.BeginInvoke(new Action(ExitFromTray)),true);tray.Theme(service.State.DarkTheme);source=HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle);source.AddHook(Hook);
   bool p=Native.RegisterHotKey(source.Handle,1,0x4003,0x50),l=Native.RegisterHotKey(source.Handle,2,0x4003,0x4C),d=Native.RegisterHotKey(source.Handle,3,0x4003,0x44);
   Ctrl<TextBlock>("HotkeyStatus").Text=p&&l&&d?"Atalhos ativos enquanto o aplicativo estiver aberto. Agem na tela selecionada.":"Um ou mais atalhos já estão em uso por outro aplicativo. Os botões continuam disponíveis.";
   refreshTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};refreshTimer.Tick+=async(s,e)=>{refreshTimer.Stop();if(busy)return;await Run(async()=>{await Task.Run(()=>service.Refresh(false));blanking.Reconcile(service.Current.Active);powerReports.Clear();Render();});};
  }
  IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled) {
   if((uint)msg==NativeTheme.ShowMessage){ShowMainWindow();handled=true;}
   if(msg==0x7e&&refreshTimer!=null){refreshTimer.Stop();refreshTimer.Start();}
   if(msg==0x312&&!busy){handled=true;if(layoutDraft!=null){Say("Aplique ou cancele a edição de disposição antes de usar esse atalho.");return IntPtr.Zero;}Window.Dispatcher.BeginInvoke(new Action(async()=>{if(w.ToInt32()==1)await Power("sleep");if(w.ToInt32()==2)await Power("wake");if(w.ToInt32()==3)await Toggle();}));}
   return IntPtr.Zero;
  }
  void Detach(){blanking.Dispose();if(tray!=null){tray.Dispose();tray=null;}if(source!=null){for(int i=1;i<=3;i++)Native.UnregisterHotKey(source.Handle,i);source.RemoveHook(Hook);}if(refreshTimer!=null)refreshTimer.Stop();}
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  void KeepWindowVisible(List<DisplayRecord> active) {
   if(preview||active.Count==0)return;var point=Window.PointToScreen(new Point(Math.Max(0,Window.ActualWidth/2),Math.Max(0,Window.ActualHeight/2)));
   if(active.Any(d=>point.X>=d.X&&point.X<d.Right&&point.Y>=d.Y&&point.Y<d.Bottom))return;
   var other=active.OrderByDescending(d=>d.Width).First();Window.WindowState=WindowState.Normal;SetWindowPos(new WindowInteropHelper(Window).Handle,IntPtr.Zero,other.X+20,other.Y+20,Math.Min(other.Width-40,1180),Math.Min(other.Height-40,860),0x14);
  }
  void MoveAwayFrom(string key) {
   if(preview||service.Current.Active.Count<2)return;var display=service.Current.Active.First(d=>Native.Same(d.Key,key));var handle=new WindowInteropHelper(Window).Handle;var monitor=Native.Monitors().Values.FirstOrDefault(m=>Native.Same(m.Item2.Device,display.DeviceName));
   if(monitor==null)return;var r=monitor.Item2.Bounds;
   // Move only this app's window if it occupies the panel that is about to sleep.
   var point=Window.PointToScreen(new Point(Math.Max(0,Window.ActualWidth/2),Math.Max(0,Window.ActualHeight/2)));
   if(point.X>=r.Left&&point.X<r.Right&&point.Y>=r.Top&&point.Y<r.Bottom){var other=service.Current.Active.Where(d=>!Native.Same(d.Key,key)).OrderByDescending(d=>d.Width).First();Window.WindowState=WindowState.Normal;SetWindowPos(handle,IntPtr.Zero,other.X+20,other.Y+20,Math.Min(other.Width-40,1180),Math.Min(other.Height-40,860),0x14);}
  }
  void Identify() {
   if(preview)return;foreach(var d in service.Current.Active){var saved=service.State.Remembered.First(r=>Native.Same(r.Key,d.Key));var overlay=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Width=180,Height=120,Background=B("Accent"),Foreground=Brushes.White,Content=new TextBlock{Text=saved.Number.ToString(),FontSize=54,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};overlay.Show();SetWindowPos(new WindowInteropHelper(overlay).Handle,new IntPtr(-1),d.X+d.Width/2-90,d.Y+d.Height/2-60,180,120,0x10);var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2.5)};timer.Tick+=(s,e)=>{timer.Stop();overlay.Close();};timer.Start();}
  }
  public void PreviewTheme(bool dark){service.State.DarkTheme=dark;Theme();Render();}
  public void PreviewPage(string name){if(name=="editor")Btn("EditLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else if(name=="settings")ShowPage("Preferências");}
  public void RenderPreview(string output,int width=1380,int height=1200) {
   var root=Ctrl<Grid>("RootView");Window.Content=null;TextElement.SetForeground(root,B("Ink"));TextElement.SetFontFamily(root,Window.FontFamily);root.Margin=new Thickness(0);root.Width=width;root.Height=height;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();RenderMap();root.UpdateLayout();
   var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(output))png.Save(stream);
  }
  static TextBlock VisualText(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);var text=child as TextBlock;if(text!=null)return text;var nested=VisualText(child);if(nested!=null)return nested;}return null;}
  public object SmokeTest() {
   var checks=new List<string>();
   Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException("UI: "+name);checks.Add(name);};
   check(Ctrl<TextBlock>("BrandTitle").Text==ProductInfo.Name,"product name displayed");
   check(Ctrl<TextBlock>("VersionLabel").Text.Contains(ProductInfo.Version),"semantic version displayed");
   check(Window.Icon!=null&&Ctrl<Image>("BrandIcon").Source!=null,"application icon embedded");
   check(Ctrl<System.Windows.Shapes.Rectangle>("AuthorSignature").OpacityMask!=null,"author signature embedded");
   check(!Btn("PowerButton").IsEnabled,"power blocked until individual compatibility is known");
   var active=service.Current.Active.First(d=>d.IsPrimary);selected=active.Key;Render();check(Ctrl<TextBlock>("SelectedModel").Text==active.DeviceName,"selection updates inspector");
   bool original=service.State.DarkTheme;Btn("ThemeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(service.State.DarkTheme!=original,"theme switch changes state");Btn("ThemeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Ctrl<ComboBox>("ProfilePicker").Text="Teste de interface";Btn("SaveProfileButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(service.State.Profiles.Any(p=>p.Name=="Teste de interface"),"save profile uses native current inventory");
   Btn("PerfisNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Ctrl<FrameworkElement>("ProfilesPage").Visibility==Visibility.Visible,"profiles navigation");
   Btn("AtalhosNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Ctrl<FrameworkElement>("ShortcutsPage").Visibility==Visibility.Visible,"shortcuts navigation");
   Btn("TelasNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Ctrl<FrameworkElement>("MonitorsPage").Visibility==Visibility.Visible,"return to monitors");
   Btn("SettingsNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Ctrl<FrameworkElement>("SettingsPage").Visibility==Visibility.Visible,"startup and tray preferences navigation");Btn("TelasNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   string saved=LayoutPlanner.Fingerprint(service.State.Remembered);Btn("EditLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(layoutDraft!=null&&Ctrl<StackPanel>("LayoutControls").Visibility==Visibility.Visible,"layout editor opens without changing hardware");
   int number=layoutDraft.Find(selected).Number;Ctrl<TextBox>("XPositionInput").Text=(layoutDraft.Displays.Max(d=>d.Right)+100).ToString();Ctrl<TextBox>("YPositionInput").Text=(layoutDraft.Displays.Max(d=>d.Bottom)+100).ToString();Ctrl<TextBox>("NumberInput").Text=number.ToString();Btn("SetPositionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(layoutDraft.Find(selected).X!=service.State.Remembered.First(d=>Native.Same(d.Key,selected)).X,"coordinate controls update the draft");
   uint rotationBefore=Native.U32(Convert.FromBase64String(layoutDraft.Find(selected).PathData),40);Btn("RotateRightButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Native.U32(Convert.FromBase64String(layoutDraft.Find(selected).PathData),40)!=rotationBefore&&Ctrl<TextBlock>("OrientationValue").Text.Contains("°"),"rotation control updates map draft and orientation label");Btn("RotateLeftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(Native.U32(Convert.FromBase64String(layoutDraft.Find(selected).PathData),40)==rotationBefore,"opposite rotation control returns the draft orientation");
   Btn("CancelLayoutButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(layoutDraft==null&&LayoutPlanner.Fingerprint(service.State.Remembered)==saved,"cancelling layout preserves saved arrangement");
   check(Ctrl<ComboBox>("TvPicker").Items.Count==service.State.Remembered.Count,"TV preference lists every remembered display");
   check(Btn("SetPrimaryDirectButton").Content.ToString()=="Principal"&&!Btn("SetPrimaryDirectButton").IsEnabled,"direct primary control identifies the current primary without entering the editor");
   if(service.State.TvKey!=null&&service.State.BeforeTv==null){var originalState=service.State;var activeList=service.Current.Active;service.State=TvModePlan.Start(originalState,activeList,originalState.TvKey);service.Current.Active=LayoutPlanner.Plan(service.State.Remembered,new[]{originalState.TvKey},originalState.TvKey);selected=originalState.TvKey;Render();check(Btn("RestoreWorkspaceButton").Visibility==Visibility.Visible&&Btn("RestoreWorkspaceButton").IsEnabled&&!Btn("TvModeButton").IsEnabled,"TV-only view offers return and protects the original backup from being replaced");service.State=originalState;service.Current.Active=activeList;selected=active.Key;Render();}
   check(Ctrl<TextBlock>("SelectedStatus").Text.IndexOf("Tela preta",StringComparison.Ordinal)<0&&blanking.Count==0,"UI tests never cover the user monitors");
   if(service.Current.Active.Count>1){var activeList=service.Current.Active;var known=service.KnownBindings;service.Current.Active=activeList.Where(d=>!Native.Same(d.Key,selected)).ToList();service.KnownBindings=known.Where(b=>!Native.Same(b.Key,selected)).ToList();Render();check(!Btn("ToggleButton").IsEnabled&&Ctrl<TextBlock>("SelectedStatus").Text.StartsWith("Sem conexão"),"physically unavailable display is not offered as a disabled output");service.KnownBindings=known;Render();check(Btn("ToggleButton").IsEnabled,"connected disabled output can be reactivated");service.Current.Active=activeList;Render();}
   check(!Ctrl<Slider>("BrightnessSlider").IsEnabled&&!Btn("ApplyBrightnessButton").IsEnabled,"brightness writes blocked until a per-monitor read succeeds");
   Ctrl<ComboBox>("ProfilePicker").ApplyTemplate();var field=(TextBox)Ctrl<ComboBox>("ProfilePicker").Template.FindName("PART_EditableTextBox",Ctrl<ComboBox>("ProfilePicker"));check(field!=null&&((SolidColorBrush)field.Background).Color==((SolidColorBrush)B("Panel")).Color,"editable profile field follows the active theme");
   new WindowInteropHelper(Window).EnsureHandle();NativeTheme.Apply(Window,service.State.DarkTheme);int nativeDark=NativeTheme.ReadDark(Window),caption=NativeTheme.ReadCaption(Window);check(nativeDark<0||nativeDark==(service.State.DarkTheme?1:0),"native title bar follows the chosen theme when supported");
   if(caption>=0){var color=((SolidColorBrush)B("Panel")).Color;check(caption==(color.R|(color.G<<8)|(color.B<<16)),"Windows 11 caption uses the panel color");}
   using(var testTray=new TrayController(()=>{},()=>{},false)){testTray.Theme(service.State.DarkTheme);check(!testTray.Visible&&testTray.MenuItems==3,"tray icon has open and quit actions and disposes without a visible test icon");}
   Ctrl<TextBlock>("Feedback").Visibility=Visibility.Collapsed;var root=Ctrl<Grid>("RootView");root.Measure(new Size(1380,1200));root.Arrange(new Rect(0,0,1380,1200));root.UpdateLayout();var primaryText=VisualText(Btn("EditLayoutButton"));check(primaryText!=null&&((SolidColorBrush)primaryText.Foreground).Color==((SolidColorBrush)B("PrimaryInk")).Color,"primary button text uses its own high-contrast foreground");
   check(Ctrl<ScrollViewer>("MainScroll").ScrollableHeight<1,"standard window fits controls without a page scrollbar");
   check(Window.WindowStyle==WindowStyle.None&&System.Windows.Shell.WindowChrome.GetWindowChrome(Window).CaptionHeight==56&&System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(Btn("WindowCloseButton")),"custom caption removes the system white strip and keeps interactive window controls");
   return new{Passed=checks.Count,Failed=0,Tests=checks,HardwareWrites=0};
  }
 }
 public sealed class ConfirmationWindow:Window {
  int remaining=15;readonly TextBlock count;
  public ConfirmationWindow() {
   Background=(Brush)Application.Current.FindResource("Panel");Foreground=(Brush)Application.Current.FindResource("Ink");SourceInitialized+=(s,e)=>NativeTheme.Apply(this,((SolidColorBrush)Background).Color.R<100);
   Title="Confirmar disposição";Width=500;Height=250;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;Topmost=true;FontFamily=new FontFamily("Segoe UI");
   var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="A disposição das telas ficou correta?",FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});count=new TextBlock{Margin=new Thickness(0,14,0,20),TextWrapping=TextWrapping.Wrap};panel.Children.Add(count);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var back=new Button{Content="Reverter",Padding=new Thickness(15,8,15,8),IsCancel=true};var keep=new Button{Content="Manter disposição",Padding=new Thickness(15,8,15,8),Margin=new Thickness(10,0,0,0),IsDefault=true};back.Click+=(s,e)=>{DialogResult=false;};keep.Click+=(s,e)=>{DialogResult=true;};buttons.Children.Add(back);buttons.Children.Add(keep);panel.Children.Add(buttons);Content=panel;
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};Action tick=()=>{count.Text="A configuração anterior volta em "+remaining+" segundos se você não confirmar.";};timer.Tick+=(s,e)=>{remaining--;tick();if(remaining<=0){timer.Stop();DialogResult=false;}};Loaded+=(s,e)=>{tick();timer.Start();};Closed+=(s,e)=>timer.Stop();
  }
 }
}
