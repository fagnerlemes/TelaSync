using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tela;

[assembly:AssemblyTitle("Instalador TelaSync")]
[assembly:AssemblyCompany(ProductInfo.Publisher)]
[assembly:AssemblyProduct(ProductInfo.Name)]
[assembly:AssemblyInformationalVersion(ProductInfo.Version)]
[assembly:AssemblyVersion(ProductInfo.AssemblyVersion)]
[assembly:AssemblyFileVersion(ProductInfo.AssemblyVersion)]
namespace TelaSetup {
 public sealed class PayloadItem { public string Name,Resource,Hash; }
 public sealed class InstalledManifest {
  public string AppId,Version,Name,Executable;
  public bool DesktopShortcut,StartMenuShortcut,Registered;
  public List<string> Files=new List<string>();
 }
 static class Package {
  public const string Marker=".telasync-install.json";
  public static string DefaultRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",ProductInfo.AppId);}}
  static string RegistryPath {get{return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\"+ProductInfo.AppId;}}
  static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}
  public static T Read<T>(string path){return Json().Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
  public static void Write(string path,object value){File.WriteAllText(path,Json().Serialize(value),new UTF8Encoding(false));}
  public static byte[] Resource(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(stream==null)throw new InvalidDataException("Recurso ausente: "+name);using(var memory=new MemoryStream()){stream.CopyTo(memory);return memory.ToArray();}}}
  public static string Hash(byte[] value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
  static string SafeFile(string root,string name){if(String.IsNullOrEmpty(name)||Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Nome de arquivo inválido no pacote.");string full=Path.GetFullPath(Path.Combine(root,name));if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Arquivo fora da pasta de instalação.");return full;}
  static void CheckRoot(string root,bool test){string full=Path.GetFullPath(root);if(test){if(!Path.GetFileName(full).StartsWith("tela-installer-test-",StringComparison.Ordinal))throw new InvalidOperationException("A pasta de teste deve começar com tela-installer-test-.");}else if(!String.Equals(full,Path.GetFullPath(DefaultRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Pasta de instalação inesperada.");}
  static List<PayloadItem> Items(){return Json().Deserialize<List<PayloadItem>>(Encoding.UTF8.GetString(Resource("Payload.Manifest")).TrimStart('\ufeff'));}
  public static object Verify(){var items=Items();foreach(var item in items)if(Hash(Resource(item.Resource))!=item.Hash)throw new InvalidDataException("Falha de integridade em "+item.Name);return new{Product=ProductInfo.Name,Version=ProductInfo.Version,Files=items.Count,HashesValid=true,Publisher=ProductInfo.Publisher};}
  static void CheckClosed(string executable) {
   foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))using(process)try{if(String.Equals(process.MainModule.FileName,executable,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Feche o "+ProductInfo.Name+" antes de instalar, atualizar ou remover.");}catch(System.ComponentModel.Win32Exception){}
  }
  static Action RegistryRollback(string root) {
   var values=new Dictionary<string,object>();var kinds=new Dictionary<string,RegistryValueKind>();bool existed;
   using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath)) {
    existed=key!=null;if(key!=null){if(!String.Equals((string)key.GetValue("InstallLocation"),root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Já existe um registro de instalação em outra pasta. Ele foi preservado.");foreach(string name in key.GetValueNames()){values[name]=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);kinds[name]=key.GetValueKind(name);}}
   }
   return ()=>{if(!existed){Registry.CurrentUser.DeleteSubKey(RegistryPath,false);return;}using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)){foreach(string name in key.GetValueNames())if(!values.ContainsKey(name))key.DeleteValue(name,false);foreach(var value in values)key.SetValue(value.Key,value.Value,kinds[value.Key]);}};
  }
  public static void Install(string root,bool desktop,bool menu,bool registration,bool test) {
   CheckRoot(root,test);Verify();string marker=SafeFile(root,Marker);InstalledManifest previous=null;
   if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()){
    if(!File.Exists(marker))throw new InvalidOperationException("A pasta contém arquivos de outra instalação. Nenhum arquivo foi substituído.");previous=Read<InstalledManifest>(marker);if(previous.AppId!=ProductInfo.AppId)throw new InvalidOperationException("A pasta pertence a outro aplicativo.");
   }
   string executable=SafeFile(root,ProductInfo.Executable);
   CheckClosed(executable);Action registryUndo=registration?RegistryRollback(root):null;bool registryTouched=false,createdRoot=!Directory.Exists(root);
   Directory.CreateDirectory(root);var backups=new Dictionary<string,byte[]>();var touched=new List<string>();var createdFolders=new List<string>();var payload=Items();
   Action<string> backup=path=>{if(backups.ContainsKey(path))return;backups[path]=File.Exists(path)?File.ReadAllBytes(path):null;touched.Add(path);};
   Action<string,string,string,bool> updateShortcut=(path,target,arguments,present)=>{backup(path);if(present)Shortcut(path,target,arguments,Path.Combine(root,"app.ico"));else RemoveShortcut(path,target);};
   try {
    foreach(var item in payload){string path=SafeFile(root,item.Name);backups[path]=File.Exists(path)?File.ReadAllBytes(path):null;touched.Add(path);File.WriteAllBytes(path,Resource(item.Resource));}
    string uninstaller=SafeFile(root,"Uninstall.exe");backups[uninstaller]=File.Exists(uninstaller)?File.ReadAllBytes(uninstaller):null;touched.Add(uninstaller);File.Copy(Assembly.GetExecutingAssembly().Location,uninstaller,true);
    backups[marker]=File.Exists(marker)?File.ReadAllBytes(marker):null;touched.Add(marker);
    var manifest=new InstalledManifest{AppId=ProductInfo.AppId,Version=ProductInfo.Version,Name=ProductInfo.Name,Executable=ProductInfo.Executable,DesktopShortcut=desktop,StartMenuShortcut=menu,Registered=registration,Files=payload.Select(i=>i.Name).Concat(new[]{"Uninstall.exe",Marker}).ToList()};Write(marker,manifest);
    if(desktop||(previous!=null&&previous.DesktopShortcut))updateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),ProductInfo.Name+".lnk"),executable,"",desktop);
    if(menu||(previous!=null&&previous.StartMenuShortcut)){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),ProductInfo.Name);if(!Directory.Exists(folder)){createdFolders.Add(folder);Directory.CreateDirectory(folder);}updateShortcut(Path.Combine(folder,ProductInfo.Name+".lnk"),executable,"",menu);updateShortcut(Path.Combine(folder,"Desinstalar "+ProductInfo.Name+".lnk"),uninstaller,"--uninstall",menu);}
    if(registration){registryTouched=true;using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)){key.SetValue("DisplayName",ProductInfo.Name);key.SetValue("DisplayVersion",ProductInfo.Version);key.SetValue("Publisher",ProductInfo.Publisher);key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",Path.Combine(root,"app.ico"));key.SetValue("UninstallString",Quote(uninstaller)+" --uninstall");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);key.SetValue("EstimatedSize",(int)(Directory.GetFiles(root).Sum(f=>new FileInfo(f).Length)/1024),RegistryValueKind.DWord);}}
    if(previous!=null)foreach(string old in previous.Files.Where(f=>!manifest.Files.Contains(f))){string path=SafeFile(root,old);backup(path);if(File.Exists(path))File.Delete(path);}
   }catch {if(registryTouched)try{registryUndo();}catch(Exception){}foreach(var path in touched.AsEnumerable().Reverse()){try{if(backups[path]==null){if(File.Exists(path))File.Delete(path);}else File.WriteAllBytes(path,backups[path]);}catch(IOException){}}foreach(string folder in createdFolders)if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);if(createdRoot&&Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);throw;}
  }
  static void Shortcut(string path,string target,string arguments,string icon) {
   object shell=null,shortcut=null;
   try {
    shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
    if(File.Exists(path)){string existing=(string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);if(!String.Equals(existing,target,StringComparison.OrdinalIgnoreCase))throw new IOException("Já existe outro atalho com o nome "+Path.GetFileName(path)+". Ele foi preservado.");}
    foreach(var pair in new Dictionary<string,string>{{"TargetPath",target},{"Arguments",arguments},{"WorkingDirectory",Path.GetDirectoryName(target)},{"IconLocation",icon},{"Description",ProductInfo.Name+" "+ProductInfo.Version+" — "+ProductInfo.Signature}})shortcut.GetType().InvokeMember(pair.Key,BindingFlags.SetProperty,null,shortcut,new object[]{pair.Value});
    shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
   }finally {if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
  }
  static void RemoveShortcut(string path,string expected) {
   if(!File.Exists(path))return;object shell=null,shortcut=null;
   try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true));shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});string target=(string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);if(String.Equals(target,expected,StringComparison.OrdinalIgnoreCase))File.Delete(path);}
   finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
  }
  public static void Remove(string root,bool test) {
   CheckRoot(root,test);var data=Read<InstalledManifest>(SafeFile(root,Marker));if(data.AppId!=ProductInfo.AppId)throw new InvalidOperationException("Identidade da instalação não confere.");
   var expected=new HashSet<string>(Items().Select(i=>i.Name).Concat(new[]{"Uninstall.exe",Marker}),StringComparer.OrdinalIgnoreCase);
   if(data.Files.Any(f=>!expected.Contains(f)))throw new InvalidDataException("Manifesto de desinstalação contém arquivos desconhecidos.");
   string exe=SafeFile(root,data.Executable),uninstaller=SafeFile(root,"Uninstall.exe");CheckClosed(exe);
   if(!test){if(data.DesktopShortcut)RemoveShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),data.Name+".lnk"),exe);if(data.StartMenuShortcut){string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),data.Name);RemoveShortcut(Path.Combine(folder,data.Name+".lnk"),exe);RemoveShortcut(Path.Combine(folder,"Desinstalar "+data.Name+".lnk"),uninstaller);if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}if(data.Registered)using(var key=Registry.CurrentUser.OpenSubKey(RegistryPath)){if(key!=null&&String.Equals((string)key.GetValue("InstallLocation"),root,StringComparison.OrdinalIgnoreCase)){key.Close();Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);}}}
   foreach(string file in data.Files){string path=SafeFile(root,file);if(File.Exists(path))File.Delete(path);}
   if(Directory.Exists(root)&&!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
   // User profiles live in LocalAppData/AppId, outside Programs/AppId, and are preserved.
  }
  public static string Quote(string value){if(value.Contains("\""))throw new ArgumentException("Caminho inválido.");return "\""+value+"\"";}
 }
 public static class SetupProgram {
  [STAThread]public static int Main(string[] args) {
   try {
    if(args.Length==2&&args[0]=="--verify"){Package.Write(args[1],Package.Verify());return 0;}
    if(args.Length==2&&args[0]=="--test-install"){Package.Install(args[1],false,false,false,true);Package.Write(args[1]+".install-result.json",new{Installed=true,RegistrationWritten=false,ShortcutsWritten=false});return 0;}
    if(args.Length==2&&args[0]=="--test-remove"){Package.Remove(args[1],true);Package.Write(args[1]+".remove-result.json",new{Removed=true,UserDataPreserved=true});return 0;}
    if(args.Length==2&&args[0]=="--test-shortcut"){string dir=Path.GetFullPath(args[1]);if(!Path.GetFileName(dir).StartsWith("tela-installer-test-",StringComparison.Ordinal))throw new InvalidOperationException("Pasta de teste inválida.");Directory.CreateDirectory(dir);TestShortcut(dir);return 0;}
    if(args.Length==2&&args[0]=="--render-preview"){var previewApp=new Application();var window=CreateWindow();var root=(FrameworkElement)window.Content;root.Width=540;root.Height=590;root.Measure(new Size(540,590));root.Arrange(new Rect(0,0,540,590));root.UpdateLayout();var bitmap=new RenderTargetBitmap(540,590,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(args[1]))encoder.Save(file);return 0;}
    if(args.Length==3&&args[0]=="--remove-owned") {string root=Path.GetFullPath(args[1]);int parentId=Int32.Parse(args[2]);try{using(var parent=Process.GetProcessById(parentId))if(!parent.WaitForExit(15000))throw new InvalidOperationException("O desinstalador original ainda está em execução.");}catch(ArgumentException){}Package.Remove(root,false);MessageBox.Show(ProductInfo.Name+" foi removido. Seus perfis foram preservados.",ProductInfo.Name);return 0;}
    if(args.Length==1&&args[0]=="--uninstall")return Uninstall();
    if(args.Length!=0)throw new ArgumentException("Opção inválida.");
    var app=new Application();return app.Run(CreateWindow());
   }catch(Exception e){if(args.Length>=2){try{File.WriteAllText(args[1]+".error.txt",e.ToString());}catch(Exception){}}else MessageBox.Show(e.Message,ProductInfo.Name,MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
  }
  static void TestShortcut(string dir){string target=Path.Combine(dir,ProductInfo.Executable);File.WriteAllBytes(target,new byte[]{0});var method=typeof(Package).GetMethod("Shortcut",BindingFlags.Static|BindingFlags.NonPublic);method.Invoke(null,new object[]{Path.Combine(dir,"test.lnk"),target,"",target});object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)),shortcut=null;try{shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(dir,"test.lnk")});string read=(string)shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null);if(!String.Equals(read,target,StringComparison.OrdinalIgnoreCase))throw new Exception("Atalho não aponta para o executável esperado.");Package.Write(Path.Combine(dir,"shortcut-result.json"),new{Target=read,Valid=true});}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}}
  static BitmapSource Image(string name){using(var stream=new MemoryStream(Package.Resource(name))){var frame=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);frame.Freeze();return frame;}}
  static Window CreateWindow() {
   var bg=new SolidColorBrush(Color.FromRgb(25,29,34));var ink=Brushes.White;var muted=new SolidColorBrush(Color.FromRgb(170,183,198));
   var window=new Window{Title="Instalar "+ProductInfo.Name,Width=540,Height=620,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=bg,Foreground=ink,FontFamily=new FontFamily("Segoe UI"),Icon=Image("Brand.Icon")};
   var panel=new StackPanel{Margin=new Thickness(32)};panel.Children.Add(new System.Windows.Controls.Image{Source=Image("Brand.Icon"),Width=66,Height=66,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,18)});panel.Children.Add(new TextBlock{Text="Instalar "+ProductInfo.Name,Foreground=ink,FontSize=26,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock{Text="Versão "+ProductInfo.Version+" · "+ProductInfo.Signature,Foreground=muted,FontSize=13,Margin=new Thickness(0,7,0,20)});panel.Children.Add(new TextBlock{Text="Instalação somente para este usuário, sem precisar de administrador.",TextWrapping=TextWrapping.Wrap,Foreground=muted,FontSize=13});panel.Children.Add(new TextBlock{Text=Package.DefaultRoot,TextWrapping=TextWrapping.Wrap,Foreground=ink,FontSize=12,Margin=new Thickness(0,12,0,20)});
   var desktop=new CheckBox{Content="Criar atalho na área de trabalho",IsChecked=true,Foreground=ink,Margin=new Thickness(0,0,0,12)};var menu=new CheckBox{Content="Criar atalhos no Menu Iniciar",IsChecked=true,Foreground=ink};panel.Children.Add(desktop);panel.Children.Add(menu);
   panel.Children.Add(new System.Windows.Shapes.Rectangle{Width=205,Height=50,HorizontalAlignment=HorizontalAlignment.Left,Fill=muted,Margin=new Thickness(0,20,0,18),OpacityMask=new ImageBrush(Image("Brand.Signature")){Stretch=Stretch.Uniform}});
   var install=new Button{Content="Instalar",Height=40,HorizontalAlignment=HorizontalAlignment.Stretch,FontSize=14};var status=new TextBlock{Foreground=muted,TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,10,0,0)};bool done=false;
   install.Click+=(s,e)=>{if(done){window.Close();return;}install.IsEnabled=false;try{Package.Install(Package.DefaultRoot,desktop.IsChecked==true,menu.IsChecked==true,true,false);status.Text="Instalação concluída. Abra o programa pelo atalho.";install.Content="Concluir";done=true;}catch(Exception ex){status.Text=ex.Message;}finally{install.IsEnabled=true;}};panel.Children.Add(install);panel.Children.Add(status);window.Content=new Border{Background=bg,Child=panel};return window;
  }
  static int Uninstall() {
   string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);if(!String.Equals(Path.GetFullPath(root),Path.GetFullPath(Package.DefaultRoot),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Execute o desinstalador instalado pelo Windows.");
   if(MessageBox.Show("Remover "+ProductInfo.Name+" e seus atalhos? Os perfis de telas serão preservados.",ProductInfo.Name,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return 0;
   string helper=Path.Combine(Path.GetTempPath(),"TelaSync-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
   Process.Start(new ProcessStartInfo{FileName=helper,Arguments="--remove-owned "+Package.Quote(root)+" "+Process.GetCurrentProcess().Id,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});return 0;
  }
 }
}
