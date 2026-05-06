using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace Sts2OrbLayout;

// Ctrl 누르면 곡선 + waypoint 마커 + 슬롯 라벨 표시.
// 입력 우선순위 (Ctrl + LMB):
//   1. waypoint 마커 위 (≤ MarkerHitRadiusPx)  → 드래그
//   2. 곡선 위 (≤ CurveHitRadiusPx)             → 클릭 위치에 새 waypoint 삽입 + 즉시 드래그
//   3. (Shift 같이 누르면) 빈 공간이어도 강제 추가 + 드래그
//   4. 그 외                                    → 무시
// Ctrl + RMB on 마커 (끝점 제외) → 제거.
// 첫 Ctrl 누름 시점에 현재 모든 orb 위치를 N 개 waypoint 로 자동 캡처 (capacity = N → N control points).
public partial class OrbDragEditor : Node
{
    public static OrbDragEditor? Instance { get; private set; }

    private NOrbManager? _activeManager;

    private int _draggingWaypointIdx = -1;
    private Vector2 _dragOffsetLocal;

    private readonly List<Polygon2D> _waypointMarkers = new();
    private readonly List<Label> _slotLabels = new();
    private Line2D? _curveLine;
    private bool _affordanceShown;
    private NOrbManager? _affordanceManager;
    private Node? _affordanceParent;

    private const float MarkerHitRadiusPx = 22f;
    private const float CurveHitRadiusPx = 18f;
    private const int MarkerSides = 24;
    private const float MarkerRadiusEndpoint = 12f;
    private const float MarkerRadiusMid = 9f;
    private static readonly Color CurveColor   = new(1f, 0.95f, 0.4f, 0.55f);
    private static readonly Color EndpointColor= new(1f, 0.95f, 0.4f);
    private static readonly Color MidColor     = new(1f, 0.7f, 0.3f);
    private static readonly Color OutlineColor = new(0f, 0f, 0f, 0.9f);

    public static void Install(SceneTree tree)
    {
        if (Instance != null) return;
        Instance = new OrbDragEditor { Name = "OrbDragEditor" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, Instance);
    }

    public override void _Process(double delta)
    {
        bool ctrl = Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Meta);
        bool shouldShow = ctrl || _draggingWaypointIdx >= 0;
        if (shouldShow) RefreshAffordance();
        else if (_affordanceShown) HideAffordance();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.CtrlPressed)
                {
                    if (TryStartDragOrAdd(mb.GlobalPosition, forceAddAnywhere: mb.ShiftPressed))
                        GetViewport().SetInputAsHandled();
                }
                else if (!mb.Pressed && _draggingWaypointIdx >= 0)
                {
                    FinishDrag();
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && mb.CtrlPressed)
            {
                if (TryRemoveWaypoint(mb.GlobalPosition))
                    GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion mm && _draggingWaypointIdx >= 0)
        {
            UpdateDrag(mm.GlobalPosition);
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------- waypoint 자동 초기화 ----------
    // 저장 없을 때 현재 모든 orb 위치를 그대로 waypoint 로 캡처.
    // capacity = N → N 개 waypoint → 슬롯 3 개 이상이면 중간 슬롯에도 컨트롤 포인트가 즉시 생긴다.
    private bool EnsureInitialized(NOrbManager mgr)
    {
        if (OrbLayoutStore.TryGetWaypoints(out _)) return true;
        int n = mgr._orbs.Count;
        if (n < 2) { MainFile.Logger.Info("[OrbLayout] capacity < 2 — 초기화 불가"); return false; }
        float scale = mgr.IsLocal ? 1f : 0.75f;
        var ws = new Vector2[n];
        for (int i = 0; i < n; i++) ws[i] = mgr._orbs[i].Position / scale;
        OrbLayoutStore.SetWaypoints(ws);
        MainFile.Logger.Info($"[OrbLayout] initialized {n} waypoints from current orb positions");
        return true;
    }

    // ---------- click handling: drag waypoint OR add on curve ----------
    private bool TryStartDragOrAdd(Vector2 globalMouse, bool forceAddAnywhere)
    {
        var mgr = FindLocalManager();
        if (mgr == null) { MainFile.Logger.Info("[OrbLayout] no local manager"); return false; }
        if (!EnsureInitialized(mgr)) return false;
        if (!OrbLayoutStore.TryGetWaypoints(out var ws)) return false;

        var parent = mgr._orbs[0].GetParent() as Control;
        if (parent == null) return false;

        float scale = mgr.IsLocal ? 1f : 0.75f;
        var localMouse = parent.GetGlobalTransform().AffineInverse() * globalMouse;

        // 1) 기존 waypoint 마커 hit
        var wsLocal = ToLocal(ws, scale);
        int wpIdx = NearestIndex(wsLocal, localMouse, MarkerHitRadiusPx);
        if (wpIdx >= 0)
        {
            return BeginDragWaypoint(mgr, wpIdx, wsLocal[wpIdx], localMouse);
        }

        // 2) 곡선 위 클릭 → 그 자리에 추가
        if (!forceAddAnywhere)
        {
            var samplesLocal = OrbCurve.SampleCurve(wsLocal);
            int sIdx = OrbCurve.FindClosestSample(samplesLocal, localMouse, CurveHitRadiusPx, out float dist);
            if (sIdx >= 0)
            {
                int seg = OrbCurve.SampleToSegment(sIdx, ws.Length);
                int insertAt = seg + 1;
                Vector2 projectedLocal = samplesLocal[sIdx];
                Vector2 logical = projectedLocal / scale;
                var newList = new List<Vector2>(ws);
                newList.Insert(insertAt, logical);
                var newWs = newList.ToArray();
                OrbLayoutStore.SetWaypoints(newWs);
                MainFile.Logger.Info($"[OrbLayout] added waypoint on curve idx={insertAt} (total={newWs.Length}, dist={dist:F1}px)");
                return BeginDragWaypoint(mgr, insertAt, projectedLocal, localMouse);
            }
            MainFile.Logger.Info("[OrbLayout] click missed waypoint and curve — Ctrl+Shift+click 로 빈 공간에도 추가 가능");
            return false;
        }

        // 3) Shift+click: 클릭 위치에 강제 추가
        Vector2 clickLogical = localMouse / scale;
        int forcedInsertAt = OrbCurve.FindBestInsertion(ws, clickLogical);
        var fNewList = new List<Vector2>(ws);
        fNewList.Insert(forcedInsertAt, clickLogical);
        var fNewWs = fNewList.ToArray();
        OrbLayoutStore.SetWaypoints(fNewWs);
        MainFile.Logger.Info($"[OrbLayout] force-added waypoint idx={forcedInsertAt} (total={fNewWs.Length})");
        return BeginDragWaypoint(mgr, forcedInsertAt, localMouse, localMouse);
    }

    private bool BeginDragWaypoint(NOrbManager mgr, int idx, Vector2 waypointLocal, Vector2 localMouse)
    {
        mgr._curTween?.Kill();
        _activeManager = mgr;
        _draggingWaypointIdx = idx;
        _dragOffsetLocal = waypointLocal - localMouse;
        return true;
    }

    private void UpdateDrag(Vector2 globalMouse)
    {
        if (_activeManager == null || _draggingWaypointIdx < 0) return;
        if (!OrbLayoutStore.TryGetWaypoints(out var ws)) return;
        var parent = _activeManager._orbs[0].GetParent() as Control;
        if (parent == null) return;

        var localMouse = parent.GetGlobalTransform().AffineInverse() * globalMouse;
        var newLocal = localMouse + _dragOffsetLocal;
        float scale = _activeManager.IsLocal ? 1f : 0.75f;
        var newLogical = newLocal / scale;

        if (_draggingWaypointIdx >= ws.Length) return;
        var copy = new Vector2[ws.Length];
        Array.Copy(ws, copy, ws.Length);
        copy[_draggingWaypointIdx] = newLogical;
        OrbLayoutStore.SetWaypoints(copy);

        ApplyImmediate(_activeManager, copy);
    }

    private void FinishDrag()
    {
        if (_activeManager != null) _activeManager.TweenLayout();
        _draggingWaypointIdx = -1;
        _activeManager = null;
    }

    // ---------- remove waypoint ----------
    private bool TryRemoveWaypoint(Vector2 globalMouse)
    {
        var mgr = FindLocalManager();
        if (mgr == null) return false;
        if (!OrbLayoutStore.TryGetWaypoints(out var ws)) return false;
        if (ws.Length <= 2) { MainFile.Logger.Info("[OrbLayout] 최소 2 waypoint 필요 — 제거 불가"); return false; }

        var parent = mgr._orbs[0].GetParent() as Control;
        if (parent == null) return false;
        float scale = mgr.IsLocal ? 1f : 0.75f;
        var localMouse = parent.GetGlobalTransform().AffineInverse() * globalMouse;
        var wsLocal = ToLocal(ws, scale);

        int idx = NearestIndex(wsLocal, localMouse, MarkerHitRadiusPx);
        if (idx < 0) return false;
        if (idx == 0 || idx == ws.Length - 1) { MainFile.Logger.Info("[OrbLayout] 끝점은 제거 불가 (이동만 가능)"); return false; }

        var newList = new List<Vector2>(ws);
        newList.RemoveAt(idx);
        OrbLayoutStore.SetWaypoints(newList.ToArray());
        MainFile.Logger.Info($"[OrbLayout] removed waypoint idx={idx} (remaining={newList.Count})");
        mgr.TweenLayout();
        return true;
    }

    // ---------- helpers ----------
    private static Vector2[] ToLocal(Vector2[] logical, float scale)
    {
        var arr = new Vector2[logical.Length];
        for (int i = 0; i < logical.Length; i++) arr[i] = logical[i] * scale;
        return arr;
    }

    private static int NearestIndex(Vector2[] points, Vector2 pos, float maxDist)
    {
        int best = -1;
        float bestDist = maxDist;
        for (int i = 0; i < points.Length; i++)
        {
            float d = (points[i] - pos).Length();
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static void ApplyImmediate(NOrbManager mgr, Vector2[] waypoints)
    {
        var orbs = mgr._orbs;
        if (orbs == null || orbs.Count == 0) return;
        var pts = OrbCurve.Distribute(waypoints, orbs.Count);
        float scale = mgr.IsLocal ? 1f : 0.75f;
        for (int i = 0; i < orbs.Count; i++)
            if (orbs[i] != null) orbs[i].Position = pts[i] * scale;
    }

    // ---------- affordance UI ----------
    private void RefreshAffordance()
    {
        var mgr = _activeManager ?? FindLocalManager();
        if (mgr == null) { HideAffordance(); return; }
        var parent = mgr._orbs[0].GetParent() as Node;
        if (parent == null) { HideAffordance(); return; }

        // 첫 Ctrl 누름 시점에 자동 초기화 (드래그 안 해도 N 개 마커 즉시 표시)
        EnsureInitialized(mgr);

        bool needRebuild = _affordanceManager != mgr || _affordanceParent != parent;
        if (needRebuild)
        {
            ClearAffordanceNodes();
            _curveLine = new Line2D
            {
                Name = "OrbLayoutCurve",
                Width = 3f,
                DefaultColor = CurveColor,
                ZIndex = 99,
                JointMode = Line2D.LineJointMode.Round,
            };
            parent.AddChild(_curveLine);
            _affordanceManager = mgr;
            _affordanceParent = parent;
        }

        // ---- 슬롯 라벨 동기화 ----
        int orbCount = mgr._orbs.Count;
        while (_slotLabels.Count < orbCount)
        {
            int i = _slotLabels.Count;
            var label = new Label
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 100,
                Text = (i + 1).ToString(),
            };
            label.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
            label.AddThemeColorOverride("font_outline_color", OutlineColor);
            label.AddThemeConstantOverride("outline_size", 6);
            label.Position = new Vector2(-8, -36);
            mgr._orbs[i].AddChild(label);
            _slotLabels.Add(label);
        }
        while (_slotLabels.Count > orbCount)
        {
            var l = _slotLabels[_slotLabels.Count - 1];
            if (GodotObject.IsInstanceValid(l)) l.QueueFree();
            _slotLabels.RemoveAt(_slotLabels.Count - 1);
        }
        for (int i = 0; i < _slotLabels.Count; i++)
            _slotLabels[i].Text = (i + 1).ToString();

        // ---- 곡선/마커 ----
        if (OrbLayoutStore.TryGetWaypoints(out var ws))
        {
            float scale = mgr.IsLocal ? 1f : 0.75f;
            var wsLocal = ToLocal(ws, scale);
            var samples = OrbCurve.SampleCurve(wsLocal);
            if (_curveLine != null && GodotObject.IsInstanceValid(_curveLine))
                _curveLine.Points = samples;
            SyncWaypointMarkers(parent, wsLocal);
        }

        _affordanceShown = true;
    }

    private void SyncWaypointMarkers(Node parent, Vector2[] wsLocal)
    {
        while (_waypointMarkers.Count < wsLocal.Length)
        {
            var poly = new Polygon2D
            {
                Name = $"OrbLayoutWP{_waypointMarkers.Count}",
                ZIndex = 101,
            };
            parent.AddChild(poly);
            _waypointMarkers.Add(poly);
        }
        while (_waypointMarkers.Count > wsLocal.Length)
        {
            var p = _waypointMarkers[_waypointMarkers.Count - 1];
            if (GodotObject.IsInstanceValid(p)) p.QueueFree();
            _waypointMarkers.RemoveAt(_waypointMarkers.Count - 1);
        }
        int last = wsLocal.Length - 1;
        for (int i = 0; i < _waypointMarkers.Count; i++)
        {
            bool isEndpoint = (i == 0 || i == last);
            var m = _waypointMarkers[i];
            float r = isEndpoint ? MarkerRadiusEndpoint : MarkerRadiusMid;
            m.Polygon = BuildCircle(r);
            m.Color = isEndpoint ? EndpointColor : MidColor;
            m.Position = wsLocal[i];
        }
    }

    private static Vector2[] BuildCircle(float radius)
    {
        var pts = new Vector2[MarkerSides];
        for (int i = 0; i < MarkerSides; i++)
        {
            float a = i * Mathf.Tau / MarkerSides;
            pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return pts;
    }

    private void HideAffordance()
    {
        ClearAffordanceNodes();
        _affordanceManager = null;
        _affordanceParent = null;
        _affordanceShown = false;
    }

    private void ClearAffordanceNodes()
    {
        foreach (var l in _slotLabels)
            if (GodotObject.IsInstanceValid(l)) l.QueueFree();
        _slotLabels.Clear();
        foreach (var m in _waypointMarkers)
            if (GodotObject.IsInstanceValid(m)) m.QueueFree();
        _waypointMarkers.Clear();
        if (_curveLine != null && GodotObject.IsInstanceValid(_curveLine)) _curveLine.QueueFree();
        _curveLine = null;
    }

    private static NOrbManager? FindLocalManager()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return null;
        return Search(tree.Root);

        static NOrbManager? Search(Node n)
        {
            if (n is NOrbManager m && m.IsLocal && m._orbs.Count > 0) return m;
            foreach (var c in n.GetChildren())
            {
                var found = Search(c);
                if (found != null) return found;
            }
            return null;
        }
    }
}
