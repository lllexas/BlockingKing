using UnityEngine;

[CreateAssetMenu(fileName = "EntityBP", menuName = "BlockingKing/Entity BP")]
public class EntityBP : ScriptableObject
{
    [Header("Stats")]
    public int health = 1;
    public int attack = 1;

    [Header("Presentation")]
    public GameObject prefab;
}
