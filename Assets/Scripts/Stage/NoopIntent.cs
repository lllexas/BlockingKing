/// <summary>
/// Explicit wait/pass intent. It advances the tick without changing the actor state.
/// </summary>
public class NoopIntent : Intent
{
    public void Setup()
    {
        Active = true;
    }
}
