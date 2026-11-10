using System.Windows.Threading;
namespace PeripheralBattery;

internal static class UiDispatch
{
    internal static bool Available(Dispatcher dispatcher)=>!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished;

    internal static bool Post(Dispatcher dispatcher,Func<bool> canRun,Action action,DispatcherPriority priority=DispatcherPriority.Normal)
    {
        if(!Available(dispatcher) || !canRun()) return false;
        try
        {
            var operation=dispatcher.BeginInvoke(priority,new Action(()=>
            {
                // Ownership and window state may change after this work was queued.
                if(Available(dispatcher) && canRun()) action();
            }));
            return operation.Status!=DispatcherOperationStatus.Aborted;
        }
        catch(InvalidOperationException) when(!Available(dispatcher)) { return false; }
    }
}
