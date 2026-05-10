using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EntityBP", menuName = "BlockingKing/Entity BP")]
public class EntityBP : ScriptableObject
{
    [Header("Stats")]
    public int health = 1;
    public int attack = 1;

    [Header("Auras")]
    [SerializeReference, TableList, HideReferenceObjectPicker]
    public List<EntityAuraDefinition> auras = new();

    [Header("Spawn (Target.Enemy)")]
    public int spawnInterval;
    public EntityBP spawnEntityBP;

    [Header("Presentation")]
    public GameObject prefab;
}
