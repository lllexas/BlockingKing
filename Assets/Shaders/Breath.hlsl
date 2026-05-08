// Breath.hlsl — 呼吸律动墙面装饰 v3
// 依赖：Noise.hlsl（需在调用方先 #include）
// 原理：子格内方形尺寸 = noiseVal × 子格尺寸（线性占据比例）
//       噪声从连续世界坐标采样（低频 → 相邻子格平滑过渡）
#ifndef BLOCKINGKING_BREATH_INCLUDED
#define BLOCKINGKING_BREATH_INCLUDED

float3 ApplyBreath(float3 albedo, float3 ws, float subSize,
    float speed, float strength, float threshold, float noiseScale,
    float3 brightColor, float3 dimColor, float colorMin, float colorMax,
    float contrast, float noiseType)
{
    // 子格中心世界坐标
    float subX = floor(ws.x / subSize);
    float subZ = floor(ws.z / subSize);
    float centerX = (subX + 0.5) * subSize;
    float centerZ = (subZ + 0.5) * subSize;

    // 子格内归一化距离 [0, 1]（0=中心, 1=边缘）
    float cx = frac(ws.x / subSize);
    float cz = frac(ws.z / subSize);
    float dx = abs(cx - 0.5) * 2.0;
    float dz = abs(cz - 0.5) * 2.0;
    float squareDist = max(dx, dz);

    // 噪声类型切换：0=值噪声(均匀分布), 1=FBM(Perlin 山脉感)
    float noiseVal;
    if (noiseType < 0.5)
        noiseVal = noise2D(float2(centerX, centerZ) * noiseScale + _Time.y * speed);
    else
        noiseVal = fbm2D(float2(centerX, centerZ) * noiseScale + _Time.y * speed, 3);
    noiseVal = pow(noiseVal, contrast);

    // 阈值截断
    float active = smoothstep(threshold, threshold + 0.05, noiseVal);
    float target = noiseVal * active;

    // 方形内部
    float fill = 1.0 - smoothstep(target - 0.02, target, squareDist);
    float breath = fill * strength;

    // noiseVal < colorMin → 全亮, > colorMax → 全暗, 中间 smoothstep
    float colorT = smoothstep(colorMin, colorMax, noiseVal);
    float3 fillColor = lerp(brightColor, dimColor, colorT);
    return lerp(albedo, fillColor, breath);
}

#endif
