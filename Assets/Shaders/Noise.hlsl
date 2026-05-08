// Perlin 2D 噪声 + 变体
// 参考: https://gist.github.com/patriciogv/0c53cab1bfc6e4f7e5d6e9b3f2d6e2b4
#ifndef BLOCKINGKING_NOISE_INCLUDED
#define BLOCKINGKING_NOISE_INCLUDED

// ── Hash ──
float2 hash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)),
               dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// ── 2D 值噪声（非 Perlin，但视觉上足够） ──
float noise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float2 a = hash2(i);
    float2 b = hash2(i + float2(1.0, 0.0));
    float2 c = hash2(i + float2(0.0, 1.0));
    float2 d = hash2(i + float2(1.0, 1.0));

    return lerp(lerp(a.x, b.x, f.x), lerp(c.x, d.x, f.x), f.y);
}

// ── 2D Perlin 噪声 ──
float perlin2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    // 四个角的随机梯度
    float2 g00 = hash2(i) * 2.0 - 1.0;
    float2 g10 = hash2(i + float2(1.0, 0.0)) * 2.0 - 1.0;
    float2 g01 = hash2(i + float2(0.0, 1.0)) * 2.0 - 1.0;
    float2 g11 = hash2(i + float2(1.0, 1.0)) * 2.0 - 1.0;

    float n00 = dot(g00, f);
    float n10 = dot(g10, f - float2(1.0, 0.0));
    float n01 = dot(g01, f - float2(0.0, 1.0));
    float n11 = dot(g11, f - float2(1.0, 1.0));

    return lerp(lerp(n00, n10, f.x), lerp(n01, n11, f.x), f.y) * 0.5 + 0.5;
}

// ── FBM（分形布朗运动，多层叠加） ──
float fbm2D(float2 p, int octaves)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    float total = 0.0;

    for (int i = 0; i < 5; i++)
    {
        if (i >= octaves) break;
        value += amplitude * perlin2D(p * frequency);
        total += amplitude;
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value / total;
}

#endif
