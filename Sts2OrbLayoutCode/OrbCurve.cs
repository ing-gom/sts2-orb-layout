using System;
using System.Collections.Generic;
using Godot;

namespace Sts2OrbLayout;

// Catmull-Rom 스플라인 + arc-length 기반 균등 분포.
// waypoints.Length == 2 → 직선 (균등 샘플링)
public static class OrbCurve
{
    public const int SamplesPerSegment = 24;

    public static Vector2[] Distribute(Vector2[] waypoints, int count)
    {
        if (waypoints == null || waypoints.Length < 2 || count <= 0)
            return Array.Empty<Vector2>();

        var samples = SampleCurve(waypoints);
        var lens = new float[samples.Length];
        for (int i = 1; i < samples.Length; i++)
            lens[i] = lens[i - 1] + (samples[i] - samples[i - 1]).Length();
        float total = lens[lens.Length - 1];

        var result = new Vector2[count];
        if (count == 1) { result[0] = SampleAt(samples, lens, total * 0.5f); return result; }
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            result[i] = SampleAt(samples, lens, total * t);
        }
        return result;
    }

    // 곡선을 SamplesPerSegment 단위로 dense 하게 샘플링. Line2D 그리기 + 곡선 hit-test 모두 사용.
    // 반환 배열의 sample index k 는 segment SampleToSegment(k, m) 에 속함.
    public static Vector2[] SampleCurve(Vector2[] w)
    {
        int m = w.Length;
        if (m < 2) return Array.Empty<Vector2>();

        var samples = new List<Vector2>(capacity: (m - 1) * SamplesPerSegment + 1);
        if (m == 2)
        {
            for (int i = 0; i <= SamplesPerSegment; i++)
            {
                float t = (float)i / SamplesPerSegment;
                samples.Add(w[0].Lerp(w[1], t));
            }
            return samples.ToArray();
        }

        for (int seg = 0; seg < m - 1; seg++)
        {
            Vector2 p0 = (seg == 0) ? 2f * w[0] - w[1] : w[seg - 1];
            Vector2 p1 = w[seg];
            Vector2 p2 = w[seg + 1];
            Vector2 p3 = (seg == m - 2) ? 2f * w[m - 1] - w[m - 2] : w[seg + 2];
            int steps = (seg == m - 2) ? SamplesPerSegment + 1 : SamplesPerSegment;
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / SamplesPerSegment;
                samples.Add(CatmullRom(p0, p1, p2, p3, t));
            }
        }
        return samples.ToArray();
    }

    public static int SampleToSegment(int sampleIdx, int waypointCount)
    {
        if (waypointCount <= 2) return 0;
        return Mathf.Min(sampleIdx / SamplesPerSegment, waypointCount - 2);
    }

    // 곡선 위 클릭 hit-test. samples 와 click pos 모두 같은 좌표계여야 함.
    // 반환: 가장 가까운 sample 의 index. distance 가 maxRadius 보다 크면 -1.
    public static int FindClosestSample(Vector2[] samples, Vector2 pos, float maxRadius, out float bestDist)
    {
        bestDist = float.MaxValue;
        int best = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            float d = (samples[i] - pos).Length();
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (bestDist > maxRadius) return -1;
        return best;
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
              2f * p1
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private static Vector2 SampleAt(Vector2[] samples, float[] lens, float target)
    {
        if (target <= 0) return samples[0];
        if (target >= lens[lens.Length - 1]) return samples[samples.Length - 1];
        int lo = 0, hi = lens.Length - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (lens[mid] < target) lo = mid; else hi = mid;
        }
        float segLen = lens[hi] - lens[lo];
        if (segLen <= 0f) return samples[lo];
        float frac = (target - lens[lo]) / segLen;
        return samples[lo].Lerp(samples[hi], frac);
    }

    public static int FindBestInsertion(Vector2[] waypoints, Vector2 pos)
    {
        if (waypoints == null || waypoints.Length < 2) return waypoints?.Length ?? 0;
        int best = waypoints.Length;
        float bestDist = float.MaxValue;
        for (int i = 1; i < waypoints.Length; i++)
        {
            float d = DistanceToSegment(waypoints[i - 1], waypoints[i], pos);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float ab2 = ab.LengthSquared();
        if (ab2 < 0.0001f) return (p - a).Length();
        float t = Mathf.Clamp((p - a).Dot(ab) / ab2, 0f, 1f);
        return (p - (a + ab * t)).Length();
    }
}
