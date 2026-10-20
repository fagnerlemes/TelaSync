using System;
using System.Collections.Generic;
using System.Linq;

namespace Tela {
 public static class TvModePlan {
  public static string ChooseTarget(IEnumerable<DisplayRecord> displays,string configured){
   var all=displays.ToList();if(all.Any(d=>Native.Same(d.Key,configured)))return configured;
   // The user's TV reports the exact friendly name SAMSUNG. Other Samsung
   // monitors report their model names; never select them by manufacturer alone.
   var candidates=all.Where(d=>Native.Same(d.Model,"SAMSUNG")).ToList();return candidates.Count==1?candidates[0].Key:null;
  }
  public static AppState Start(AppState state,List<DisplayRecord> active,string tvKey){
   if(state.BeforeTv!=null)throw new InvalidOperationException("Volte à mesa antes de iniciar outro modo TV. A disposição anterior continua guardada.");
   if(!state.Remembered.Any(d=>Native.Same(d.Key,tvKey)))throw new InvalidOperationException("Escolha a TV em Preferências.");
   if(active.Count==0)throw new InvalidOperationException("Não há uma disposição atual para guardar.");
   var updated=Json.Clone(state);var snapshot=Json.Clone(state);MemoryLayout.Merge(snapshot,active,true);
   updated.TvKey=tvKey;updated.BeforeTv=new NamedProfile{Name="Antes do modo TV",PrimaryKey=active.First(d=>d.IsPrimary).Key,Displays=snapshot.Remembered.Select(d=>d.Copy()).ToList(),Enabled=active.Select(d=>d.Key).ToList()};
   updated.PowerRequestedOff.RemoveAll(k=>Native.Same(k,tvKey));
   return updated;
  }
  public static AppState Restore(AppState state){
   if(state.BeforeTv==null)throw new InvalidOperationException("Não há um modo TV para desfazer.");
   var updated=Json.Clone(state);var prior=updated.BeforeTv;
   if(prior.Enabled.Count==0||!prior.Enabled.Any(k=>Native.Same(k,prior.PrimaryKey)))throw new InvalidOperationException("A disposição anterior do modo TV está incompleta.");
   foreach(var d in prior.Displays){updated.Remembered.RemoveAll(r=>Native.Same(r.Key,d.Key));updated.Remembered.Add(d.Copy());}
   updated.PrimaryKey=prior.PrimaryKey;updated.PowerRequestedOff.RemoveAll(k=>prior.Enabled.Any(e=>Native.Same(k,e)));updated.BeforeTv=null;return updated;
  }
 }
}
