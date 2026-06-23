namespace TecFuelMix.Core;

public static class InvocationTimeout
{
    public static CancellationTokenSource Create(TimeSpan remainingTime, TimeSpan safetyBuffer)
    {
        var timeout = new CancellationTokenSource();
        if (remainingTime <= safetyBuffer)
        {
            timeout.Cancel();
            return timeout;
        }

        timeout.CancelAfter(remainingTime - safetyBuffer);
        return timeout;
    }
}
