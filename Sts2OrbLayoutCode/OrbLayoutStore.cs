using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace Sts2OrbLayout;

// 사용자가 구성한 곡선의 waypoint 배열을 영구 저장.
// 저장 위치: OS.GetUserDataDir() (Godot user:// 디렉토리, OS 사용자 데이터 폴더) — 게임 재시작/모드 재로딩 후에도 유지.
// 좌표는 IsLocal 매니저 기준의 logical 좌표 (원격 매니저는 0.75 배 스케일 적용 전 값).
public static class OrbLayoutStore
{
    private const string FileName = "orb_curve.json";

    private static Vector2[]? _waypoints;
    private static bool _loaded;

    public static string ConfigPath
    {
        get
        {
            var dir = OS.GetUserDataDir();
            return Path.Combine(dir, "Sts2OrbLayout", FileName);
        }
    }

    private class CurveDto
    {
        public float[][]? waypoints { get; set; }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            var dto = JsonSerializer.Deserialize<CurveDto>(json);
            if (dto?.waypoints == null || dto.waypoints.Length < 2) return;
            var arr = new Vector2[dto.waypoints.Length];
            for (int i = 0; i < dto.waypoints.Length; i++)
            {
                var p = dto.waypoints[i];
                if (p == null || p.Length < 2) return;
                arr[i] = new Vector2(p[0], p[1]);
            }
            _waypoints = arr;
            MainFile.Logger.Info($"[OrbLayout] loaded curve: {arr.Length} waypoints from {ConfigPath}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[OrbLayout] load failed: {ex.Message}");
        }
    }

    public static bool TryGetWaypoints(out Vector2[] waypoints)
    {
        EnsureLoaded();
        if (_waypoints != null && _waypoints.Length >= 2)
        {
            waypoints = _waypoints;
            return true;
        }
        waypoints = Array.Empty<Vector2>();
        return false;
    }

    public static void SetWaypoints(Vector2[] waypoints)
    {
        EnsureLoaded();
        if (waypoints == null || waypoints.Length < 2)
        {
            MainFile.Logger.Warn("[OrbLayout] refused to save: need >= 2 waypoints");
            return;
        }
        var copy = new Vector2[waypoints.Length];
        Array.Copy(waypoints, copy, waypoints.Length);
        _waypoints = copy;
        Save();
    }

    public static void Reset()
    {
        EnsureLoaded();
        _waypoints = null;
        try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); }
        catch (Exception ex) { MainFile.Logger.Warn($"[OrbLayout] reset failed: {ex.Message}"); }
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            var ws = _waypoints!;
            var dto = new CurveDto
            {
                waypoints = new float[ws.Length][],
            };
            for (int i = 0; i < ws.Length; i++)
                dto.waypoints[i] = new[] { ws[i].X, ws[i].Y };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[OrbLayout] save failed: {ex.Message}");
        }
    }
}
