using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelFeatureFilter", menuName = "BlockingKing/Level/Feature Filter")]
public sealed class LevelFeatureFilterSO : ScriptableObject
{
    [MinMaxSlider(1, 50, true), LabelText("Width")]
    public Vector2 widthRange = new(1, 50);

    [MinMaxSlider(1, 50, true), LabelText("Height")]
    public Vector2 heightRange = new(1, 50);

    [MinMaxSlider(1, 2500, true), LabelText("Area")]
    public Vector2 areaRange = new(1, 2500);

    [MinMaxSlider(0f, 1f, true), LabelText("Wall Rate")]
    public Vector2 wallRateRange = new(0f, 1f);

    [MinMaxSlider(0, 500, true), LabelText("Effective Boxes")]
    public Vector2 effectiveBoxRange = new(0, 50);
}
