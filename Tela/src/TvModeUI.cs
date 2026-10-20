using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Tela {
 public sealed partial class DesktopApp {
  void InitializeTvMode(){
   Btn("TvModeButton").Click+=async(s,e)=>{if(busy||layoutDraft!=null||preview)return;if(String.IsNullOrEmpty(service.State.TvKey)){ShowPage("Preferências");Say("Escolha a tela usada como TV em Modo TV.");return;}await Change(()=>service.PrepareTvMode(),"Modo TV confirmado. Somente a TV está ativa e principal. Use Voltar à mesa para recuperar a disposição anterior.");};
   Btn("RestoreWorkspaceButton").Click+=async(s,e)=>{if(busy||layoutDraft!=null||preview)return;await Change(()=>service.PrepareReturnFromTv(),"Mesa restaurada com as telas, a principal e as posições anteriores ao modo TV.");};
   Btn("SetPrimaryDirectButton").Click+=async(s,e)=>{if(busy||layoutDraft!=null||preview)return;string key=selected;await Change(()=>service.PreparePrimary(key),"Tela principal confirmada. As outras telas permaneceram ativas.");};
   Ctrl<ComboBox>("TvPicker").SelectionChanged+=(s,e)=>{if(updating)return;var item=Ctrl<ComboBox>("TvPicker").SelectedItem as ComboBoxItem;if(item==null)return;service.State.TvKey=(string)item.Tag;service.Store.Save(service.State);RenderTvMode();};
  }
  void RenderTvMode(){
   var picker=Ctrl<ComboBox>("TvPicker");updating=true;try{picker.Items.Clear();foreach(var d in service.State.Remembered.OrderBy(d=>d.Number)){var item=new ComboBoxItem{Content=d.Number+" · "+d.Name+" · "+d.Width+" × "+d.Height,Tag=d.Key};picker.Items.Add(item);if(Native.Same(d.Key,service.State.TvKey))picker.SelectedItem=item;}}finally{updating=false;}
   var tv=service.State.Remembered.FirstOrDefault(d=>Native.Same(d.Key,service.State.TvKey));bool session=service.State.BeforeTv!=null,available=tv!=null&&service.IsAvailable(tv.Key);
   Btn("TvModeButton").Content=session?"Modo TV ativo":tv==null?"Escolher TV":"Jogar na TV";
   Btn("TvModeButton").ToolTip=tv==null?"Escolha a TV em Preferências.":available?"Usar somente "+tv.Name+" como tela principal. As demais telas serão desativadas; Voltar à mesa recupera a disposição anterior.":"Ligue ou conecte a TV e clique em Atualizar.";
   Btn("TvModeButton").IsEnabled=!busy&&layoutDraft==null&&!session&&(tv==null||available);
   Btn("RestoreWorkspaceButton").Visibility=session&&page=="Telas"?Visibility.Visible:Visibility.Collapsed;Btn("RestoreWorkspaceButton").IsEnabled=!busy&&layoutDraft==null&&session;
   Btn("TvModeButton").Visibility=page=="Telas"?Visibility.Visible:Visibility.Collapsed;
   Ctrl<TextBlock>("TvModeStatus").Text=session?"A disposição anterior está guardada. Voltar à mesa continua disponível mesmo após fechar e abrir o programa.":tv==null?"Selecione a TV para habilitar o atalho Jogar na TV.":"Jogar na TV deixará somente "+tv.Name+" ativa e principal. A tela pequena da Intel também será desativada nesse modo.";
   picker.IsEnabled=!busy&&!session;
   var live=service.Current.Active.FirstOrDefault(d=>Native.Same(d.Key,selected));Btn("SetPrimaryDirectButton").Content=live!=null&&live.IsPrimary?"Principal":"Tornar principal";Btn("SetPrimaryDirectButton").IsEnabled=!busy&&layoutDraft==null&&live!=null&&!live.IsPrimary;Btn("SetPrimaryDirectButton").ToolTip=live!=null&&live.IsPrimary?"Esta já é a tela principal.":"Definir "+Selected.Name+" como principal, mantendo as outras telas ativas.";
  }
 }
}
