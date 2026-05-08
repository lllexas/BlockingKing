/// <summary>
/// Intent 基类。具体子类后续定义，先搭池子。
/// </summary>
public abstract class Intent
{
    public bool Active { get; set; }

    public virtual void Reset()
    {
        Active = false;
    }
}
