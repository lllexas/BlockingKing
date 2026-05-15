using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EntityBP", menuName = "BlockingKing/Stage/Entity BP")]
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
    public Mesh instancedMesh;
    public Material instancedMaterial;
    public Vector3 visualScale = Vector3.one;

    [Header("Unit Label")]
    public bool showUnitLabel;
    public string unitLabelText;
    public Color unitLabelColor = Color.white;
    public Vector3 unitLabelOffset = Vector3.zero;
    public float unitLabelScale = 1f;
}
