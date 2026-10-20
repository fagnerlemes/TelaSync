namespace Tela {
 public static class ClosePolicy {
  // Cancel means "ask" here; cancellation is never saved as a close action.
  public static CloseChoice Action(AppState state){return state.AskBeforeClose?CloseChoice.Cancel:state.RememberedCloseChoice==CloseChoice.Exit?CloseChoice.Exit:CloseChoice.Tray;}
  public static void Remember(AppState state,CloseChoice choice,bool remember){
   if(!remember||(choice!=CloseChoice.Tray&&choice!=CloseChoice.Exit))return;
   state.RememberedCloseChoice=choice;state.AskBeforeClose=false;
  }
 }
}
