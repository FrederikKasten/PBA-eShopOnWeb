namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// "Dørklokke" mellem OrderPlacementService og OutboxDispatcher i samme proces.
/// Når en ordre er gemt, ringes der på, så beskeden sendes med det samme i stedet for ved næste runde.
/// Ringer flere på samtidig, er én ringning nok: dispatcheren sender alle ventende beskeder i én omgang.
/// </summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // En anden tråd nåede at ringe på imens. Det er nok.
        }
    }

    // true = der blev ringet på; false = ventetiden løb ud.
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _signal.WaitAsync(timeout, cancellationToken);
    }
}
