// NoiseFill.hlsl — 墙面噪声方块填充（存档，暂未启用）
// 依赖：Noise.hlsl
// 用法：albedo = ApplyNoiseFill(albedo, wallH, ws.y, subSize, ...);
#ifndef BLOCKINGKING_NOISEFILL_INCLUDED
#define BLOCKINGKING_NOISEFILL_INCLUDED

float3 ApplyNoiseFill(float3 albedo, float wallH, float wsY, float subSize,
    float speed, float threshold, float strength, float3 fillColor)
{
    float subCellX = floor(wallH / subSize);
    float subCellY = floor(wsY / subSize);
    float noiseVal = perlin2D(float2(subCellX, subCellY) * 0.8 + _Time.y * speed);
    float fillMask = step(threshold, noiseVal);

    float localX = frac(wallH / subSize);
    float localY = frac(wsY / subSize);
    float inset = 0.12;
    float inSquare = step(inset, localX) * step(inset, 1.0 - localX)
                   * step(inset, localY) * step(inset, 1.0 - localY);
    float squareFill = fillMask * inSquare * strength;

    return lerp(albedo, fillColor, squareFill);
}

#endif
