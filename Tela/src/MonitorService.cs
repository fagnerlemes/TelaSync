using System;
using System.Collections.Generic;
using System.Linq;

namespace Tela {
 public sealed class PreparedChange {
  public NativeTopology Before;
  public TopologyImage Proposed;
  public List<DisplayRecord> Desired;
  public AppState NewState;
  public bool RequireConfirmation=true;
 }
 public sealed class MonitorService {
  public readonly StateStore Store;
  public AppState State;
  public NativeTopology Current;
  public List<PathBinding> KnownBindings=new List<PathBinding>();
  public MonitorService(string directory){Store=new StateStore(directory);State=Store.Load();}
  public void Refresh(bool discover) {
   // Always refresh availability as well as activity. A physically powered-off
   // device cannot be re-enabled like a connected, logically disabled output.
   Current=Native.Capture(true);KnownBindings=Current.Bindings;
   State.PowerRequestedOff.RemoveAll(k=>PowerPolicy.SleepIssue(k)!=null);
   var ordered=Current.Active.OrderByDescending(d=>d.IsPrimary).ThenByDescending(d=>d.Width).ToList();MemoryLayout.Merge(State,ordered,false);
   if(discover)foreach(var binding in Current.Bindings.Where(b=>b.Available&&!b.Active).GroupBy(b=>b.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First())) {
    if(State.Remembered.Any(d=>Native.Same(d.Key,binding.Key)))continue;
    try {var d=Native.Preferred(binding,Current.Active.Max(a=>a.Right));d.Number=State.Remembered.Max(r=>r.Number)+1;State.Remembered.Add(d);}catch(Exception e){Store.Log("Modo preferido indisponível: "+binding.Name+": "+e.Message);}
   }
   State.TvKey=TvModePlan.ChooseTarget(State.Remembered,State.TvKey);
   if(State.Profiles.Count==0)State.Profiles.Add(new NamedProfile{Name="Trabalho",PrimaryKey=State.PrimaryKey,Displays=State.Remembered.Select(d=>d.Copy()).ToList(),Enabled=Current.Active.Select(d=>d.Key).ToList()});
   Store.Save(State);
  }
  public bool IsActive(string key) {return Current.Active.Any(d=>Native.Same(d.Key,key));}
  public bool IsAvailable(string key){return KnownBindings.Any(b=>Native.Same(b.Key,key)&&b.Available);}
  public PreparedChange PrepareToggle(string key) {
   var live=Native.Capture(true);RequireExtended(live);
   bool reactivating=!live.Active.Any(d=>Native.Same(d.Key,key));
   if(reactivating&&!live.Bindings.Any(b=>Native.Same(b.Key,key)&&b.Available))throw new InvalidOperationException("O Windows não está detectando esta tela. Ligue-a pelo botão físico ou confira o cabo e clique em Atualizar.");
   var updated=Json.Clone(State);
   bool external=updated.LastAppliedFingerprint!=LayoutPlanner.Fingerprint(live.Active);
   // A missing display can cause Windows to compact the remaining desktop.
   // Keep the canonical layout when bringing it back; adopt external edits via
   // Registrar a disposição atual or the layout editor instead.
   MemoryLayout.Merge(updated,live.Active,!updated.PreservePositions||(external&&!reactivating));
   var enabled=live.Active.Select(d=>d.Key).ToList();if(enabled.Any(k=>Native.Same(k,key)))enabled.RemoveAll(k=>Native.Same(k,key));else {
    enabled.Add(key);
    if(!updated.PreservePositions){var d=updated.Remembered.First(r=>Native.Same(r.Key,key));d.X=live.Active.Max(r=>r.Right);d.Y=0;d.IsPrimary=false;}
   }
   return Prepare(live,updated,enabled,updated.PrimaryKey);
  }
  public PreparedChange PrepareProfile(NamedProfile profile) {
   var live=Native.Capture(true);RequireExtended(live);var updated=Json.Clone(State);
   foreach(var d in profile.Displays){updated.Remembered.RemoveAll(r=>Native.Same(r.Key,d.Key));updated.Remembered.Add(d.Copy());}
   updated.PrimaryKey=profile.PrimaryKey;return Prepare(live,updated,profile.Enabled,profile.PrimaryKey);
  }
  public PreparedChange PrepareTvMode(){
   var live=Native.Capture(true);RequireExtended(live);string key=State.TvKey;
   if(!live.Bindings.Any(b=>Native.Same(b.Key,key)&&b.Available))throw new InvalidOperationException("Ligue ou reconecte a TV e clique em Atualizar. Nenhuma tela foi desativada.");
   var updated=TvModePlan.Start(State,live.Active,key);var change=Prepare(live,updated,new[]{key},key);change.RequireConfirmation=false;return change;
  }
  public PreparedChange PrepareReturnFromTv(){
   var live=Native.Capture(true);RequireExtended(live);var enabled=State.BeforeTv==null?null:State.BeforeTv.Enabled;var updated=TvModePlan.Restore(State);var change=Prepare(live,updated,enabled,updated.PrimaryKey);change.RequireConfirmation=false;return change;
  }
  public PreparedChange PreparePrimary(string key){
   var live=Native.Capture(true);RequireExtended(live);if(!live.Active.Any(d=>Native.Same(d.Key,key)))throw new InvalidOperationException("Ative esta tela antes de defini-la como principal, ou use Jogar na TV.");
   var updated=Json.Clone(State);MemoryLayout.Merge(updated,live.Active,true);var editor=new LayoutEditor(updated.Remembered,key);updated.Remembered=editor.Normalize();updated.PrimaryKey=key;return Prepare(live,updated,live.Active.Select(d=>d.Key),key);
  }
  public PreparedChange PrepareArrangement(LayoutEditor editor) {
   var live=Native.Capture(true);RequireExtended(live);var updated=Json.Clone(State);
   if(!live.Active.Any(d=>Native.Same(d.Key,editor.PrimaryKey)))throw new InvalidOperationException("A tela principal precisa estar ativa.");
   updated.Remembered=editor.Normalize();updated.PrimaryKey=editor.PrimaryKey;
   var desired=LayoutPlanner.Plan(updated.Remembered,live.Active.Select(d=>d.Key),updated.PrimaryKey);
   foreach(var d in desired){var saved=updated.Remembered.First(r=>Native.Same(r.Key,d.Key));saved.X=d.X;saved.Y=d.Y;}
   return Prepare(live,updated,live.Active.Select(d=>d.Key),updated.PrimaryKey);
  }
  static void RequireExtended(NativeTopology live) {
   var active=live.Bindings.Where(b=>b.Active).ToList();if(active.Select(b=>b.SourceKey).Distinct().Count()!=active.Count)throw new InvalidOperationException("Esta primeira versão altera layouts no modo estendido. Desfaça a duplicação nas Configurações do Windows antes de continuar.");
  }
  static PreparedChange Prepare(NativeTopology live,AppState state,IEnumerable<string> enabled,string primary) {
   var desired=LayoutPlanner.Plan(state.Remembered,enabled,primary);var image=Native.Build(desired,live.Bindings);
   Native.Check(Native.Configure(image,true,false),"O Windows não validou o layout solicitado");
   return new PreparedChange{Before=live,Desired=desired,Proposed=image,NewState=state};
  }
  public void Accept(PreparedChange change) {
   var actual=Native.Capture(false);if(LayoutPlanner.Fingerprint(actual.Active)!=LayoutPlanner.Fingerprint(change.Desired))throw new InvalidOperationException("O Windows retornou posições diferentes do plano.");
   if(change.NewState.BeforeTv==null)foreach(var saved in change.NewState.Remembered.Where(d=>d.InitialPosition)){var live=actual.Active.FirstOrDefault(d=>Native.Same(d.Key,saved.Key));if(live!=null){saved.X=live.X;saved.Y=live.Y;saved.InitialPosition=false;}}
   change.NewState.LastAppliedFingerprint=LayoutPlanner.Fingerprint(actual.Active);State=change.NewState;Current=actual;Store.Save(State);
  }
  public void SaveProfile(string name) {
   if(String.IsNullOrWhiteSpace(name)||name.Trim().Length>60)throw new InvalidOperationException("Dê um nome ao perfil, com até 60 caracteres.");
   var live=Native.Capture(false);var snapshot=Json.Clone(State);MemoryLayout.Merge(snapshot,live.Active,true);
   State.Profiles.RemoveAll(p=>Native.Same(p.Name,name.Trim()));State.Profiles.Add(new NamedProfile{Name=name.Trim(),PrimaryKey=live.Active.First(d=>d.IsPrimary).Key,Displays=snapshot.Remembered.Select(d=>d.Copy()).ToList(),Enabled=live.Active.Select(d=>d.Key).ToList()});Store.Save(State);
  }
  public void RememberCurrent() {
   var live=Native.Capture(false);MemoryLayout.Merge(State,live.Active,true);State.LastAppliedFingerprint=LayoutPlanner.Fingerprint(live.Active);Store.Save(State);Current=live;
  }
 }
}
