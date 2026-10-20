using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tela {
 // Visual blanking is independent of panel power and display configuration.
 public sealed class BlankingController:IDisposable {
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  readonly Dictionary<string,Window> panels=new Dictionary<string,Window>(StringComparer.OrdinalIgnoreCase);
  public event Action Changed;
  public bool Contains(string key){return panels.ContainsKey(key);}
  public int Count {get{return panels.Count;}}
  public void Show(string key,Native.Rect bounds){
   if(Contains(key))return;if(bounds.Right<=bounds.Left||bounds.Bottom<=bounds.Top)throw new InvalidOperationException("Área da tela inválida.");
   var panel=new Window{Title="TelaSync — Tela preta",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=Brushes.Black,Focusable=false,Cursor=System.Windows.Input.Cursors.None,Width=1,Height=1};
   panel.MouseDown+=(s,e)=>{e.Handled=true;};panel.KeyDown+=(s,e)=>{e.Handled=true;};
   panel.SourceInitialized+=(s,e)=>{var source=HwndSource.FromHwnd(new WindowInteropHelper(panel).Handle);source.AddHook((IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});};
   panels.Add(key,panel);
   try{panel.Show();var handle=new WindowInteropHelper(panel).Handle;if(!SetWindowPos(handle,new IntPtr(-1),bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,0x50))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(!IsWindowVisible(handle))throw new InvalidOperationException("A janela de tela preta não ficou visível. Nenhuma conexão foi alterada.");}
   catch{panels.Remove(key);panel.Close();throw;}
   Notify();
  }
  public bool Hide(string key){Window panel;if(!panels.TryGetValue(key,out panel))return false;panels.Remove(key);panel.Close();Notify();return true;}
  public void Reconcile(IEnumerable<DisplayRecord> active){
   var displays=active.ToList();foreach(var key in panels.Keys.ToList()){
    var d=displays.FirstOrDefault(r=>Native.Same(r.Key,key));if(d==null||displays.Count(r=>Native.Same(r.DeviceName,d.DeviceName))!=1){Hide(key);continue;}
    // An unchanged display notification must not wake a blacked-out screen.
    // Keep the mask attached to its identity if Windows changes its geometry.
    if(!SetWindowPos(new WindowInteropHelper(panels[key]).Handle,new IntPtr(-1),d.X,d.Y,d.Width,d.Height,0x50))Hide(key);
   }
  }
  public void HideAll(){if(panels.Count==0)return;var open=panels.Values.ToList();panels.Clear();foreach(var panel in open)panel.Close();Notify();}
  void Notify(){if(Changed!=null)Changed();}
  public void Dispose(){HideAll();}
 }
}
