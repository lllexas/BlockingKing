/// <summary>
/// 手动Tick推进器。外部调用 PushTick()，所有订阅者执行各自的Tick逻辑。
/// </summary>
public static class TickSystem
{
    public static event System.Action OnTick;

    public static void PushTick()
    {
        OnTick?.Invoke();
    }
}
