# 별자리 / Monster 시각화 작업 기록 (롤백 됨)

작성일: 2026-05-10
범위: v0.5.0 이후 별자리 매칭 + monster 시각화 시도. 사용자 결정으로 **모두 롤백**, 다음에 다른 접근 검토 예정.

## 시도한 접근 (시간 순)

### 1단계: 별자리 매칭 인프라 (Procrustes)
- `constellations.json` (5종: 여름 대삼각형, 남십자성, 카시오페이아, 사자자리 낫, 북두칠성)
- `ConstellationMatcher` — Procrustes 정렬, 4-variant (reverse × x-flip), RMSD 0.12 임계값
- `ConstellationStore` — 활성 매칭 + Changed/Updated 이벤트
- `ConstellationLibrary` — embedded JSON 1회 로드
- `ConstellationData` — 정규화 캐시 (centroid 0, Frobenius norm 1)

### 2단계: 시각화 — ripple 효과
- `ConstellationBackground` — orb 매칭 시 visual layer
- 시도한 형태: 별 모양 outline → 구체 ring + sparkle 점 → expanding ripple
- 최종은 ripple expansion (구체 크기에서 시작 → fade out)

### 3단계: 배경 이미지 (procedural PNG)
- Python PIL 으로 별자리 그림 생성 (`scripts/render_constellations.py`)
- 256×256 PNG embedded resource
- 사용자 평가: "촌스러움" → 폐기

### 4단계: 게임 monster 자산 (놀라운 발견)
- `res://animations/monsters/{name}/{name}_skel_data.tres` — Spine SkeletonDataResource
- `res://scenes/creature_visuals/{name}.tscn` — NCreatureVisuals 씬 (100+ 종)
- `res://images/ancients/{name}_placeholder.png` — Ancient placeholder 일러스트

### 5단계: Monster Spine 통합 시도 (실패 → 롤백)
**핵심 발견**:
- NCreatureVisuals 안의 `'Visuals'` 노드가 native class **`SpineSprite`**
- `GetType().Name = "Node2D"` (wrapper) ≠ `GetClass() = "SpineSprite"` (native) — reflection 시 native class 봐야 함
- `set_skeleton_data_res()` 호출로 binding 가능
- `state.set_animation(name, loop, track)` 시그니처 (spine_godot)
- `corpse_slug` 의 animation: `attack, attack_heavy, devour_*, die, hurt, idle_loop` (10개)

**시도한 흐름**:
1. `MonsterSceneMap` — 별자리 → creature_visuals scene 매핑
2. `MonsterAssetMap` — fallback PNG (ancient placeholder)
3. `MonsterAnimator` — SpineSprite 발견 + skel_data binding + set_animation
4. PlayAttack/PlayIdle 후보 chain (case-insensitive 매칭)
5. attack 후 idle 전환: timer 기반 vs `add_animation` 큐 둘 다 시도

**최종 사용자 평가**:
- "그냥 이미지 뒤에 monster 가 보이는 것 같은데" → PNG 가 spine 위 덧씌움
- PNG 제거 후: idle 만 보이고 attack 모션 안 보임
- 큐 시도 후: 아무 animation 도 안 보임 (add_animation signature 의 float 인자 잘못, silent 실패 의심)
- 결론: "다른 방법으로 생각해봐야겠다" → 롤백 결정

## 롤백 시점에 작성된 파일들

### 신규 (untracked)
- `Sts2OrbLayoutCode/Constellations/ConstellationData.cs`
- `Sts2OrbLayoutCode/Constellations/ConstellationLibrary.cs`
- `Sts2OrbLayoutCode/Constellations/ConstellationMatcher.cs`
- `Sts2OrbLayoutCode/Constellations/ConstellationStore.cs`
- `Sts2OrbLayoutCode/Constellations/ConstellationBackground.cs`
- `Sts2OrbLayoutCode/Constellations/constellations.json`
- `Sts2OrbLayoutCode/Constellations/MonsterAssetMap.cs`
- `Sts2OrbLayoutCode/Constellations/MonsterSceneMap.cs`
- `Sts2OrbLayoutCode/Constellations/MonsterAnimator.cs`
- `Sts2OrbLayoutCode/Constellations/GameAssetProbe.cs`
- `Sts2OrbLayoutCode/Constellations/OrbLayoutConfig.cs`
- `Sts2OrbLayoutCode/ModConfigBridge.cs`

### 수정 (modified)
- `Sts2OrbLayout.csproj` — embedded resources, glob
- `Sts2OrbLayout.json` — author 필드만 (kl95 → inggom 유지)
- `Sts2OrbLayoutCode/MainFile.cs` — Constellation/ModConfig install
- `Sts2OrbLayoutCode/OrbDragEditor.cs` — 별자리 활성 시 평행이동 + waypoint 추가 차단/해제
- `Sts2OrbLayoutCode/OrbLayoutStore.cs` — 매칭 갱신 트리거
- `Sts2OrbLayoutCode/TweenLayoutPatch.cs` — capacity 변경 시 매칭 재계산

## 다음 시도 시 재활용 가능한 발견

### Spine 자산 경로 패턴 (확정)
```
res://animations/monsters/{name}/{name}_skel_data.tres   # Spine SkeletonDataResource
res://animations/monsters/{name}/{name}.atlas            # Atlas
res://animations/monsters/{name}/{name}.skel             # Skeleton binary
res://scenes/creature_visuals/{name}.tscn                # NCreatureVisuals 씬
res://images/monsters/{name}.png                          # Static placeholder
res://images/ancients/{ancient}_placeholder.png           # 6개 ancient
```

### Spine API 호출 (검증됨)
```csharp
// 정확한 시그니처
spine.Call("set_skeleton_data_res", skelData);
spine.Call("on_skeleton_data_changed");
state = spine.Call("get_animation_state").AsGodotObject();
state.Call("set_animation", "attack_heavy", false, 0);  // (name, loop, track)
state.Call("add_animation", "idle_loop", true, 0.0f, 0); // (name, loop, delay-FLOAT, track) ← float 중요
// 진단
skelData.Call("is_skeleton_data_loaded").AsBool();
skelData.Call("get_animations") → Array<SpineAnimation>
animation.Call("get_name") → String
```

### 매칭 작동 (확정)
- Procrustes RMSD 0.12 임계값 적정 (여름 대삼각형 RMSD 0.04~0.12 범위에서 매칭)
- Reverse/Flip variant 4종 시도해 best 선택
- 사용자 waypoint 변경 시 즉시 재매칭 (`OrbLayoutStore.SetWaypoints` → `RefreshMatch`)

### 미해결 / 다음 고민거리
1. **NCreatureVisuals 인스턴스화만으로는 spine setup 안 됨** — Creature 모델 ref 가 필요한 듯. instantiate + skel_data 강제 binding 으로 mesh 그리기까지는 됐으나 안정적 animation 재생은 ✗
2. **Animation transition 자연스럽지 않음** — set_animation/add_animation/timer 다 시도했으나 attack→idle 매끄럽게 안 됨
3. **PNG 와 spine 동시 표시 시 z-order 충돌** — PNG 절대 z=90, spine z=88 둘 다 ZAsRelative=false 였지만 시각적으로 깔끔하지 않음
4. **'Orbs' Control 의 clipping** — sprite/scene 추가 위치가 부모 Control 의 clip_contents 영향 받을 수 있음 (높은 절대 z 로 회피 시도했음)

### 가능한 다음 접근
- Spine 직접 인스턴스화 (NCreatureVisuals 우회) — `new SpineSprite()` 로 생성 + skel_data + atlas binding
- 또는 정적 PNG 만 사용 — animation 포기, 깔끔한 일러스트 위주
- 또는 Combat 중 실제 monster 등장 후 그 인스턴스를 reflection 으로 mirror
- 또는 별도 CanvasLayer 만들어 z 충돌 회피
- 또는 별자리 시각화 자체를 portrait 식 (작은 일러스트 카드) 로 변경

## ModConfig 옵션 (작업 기록만, 롤백)
- 4 entries: effects on/off, bg image on/off, ripple speed, ripple range
- `tree.CreateTimer(0.0).Timeout += TryRegister` 로 deferred 등록 (다른 모드 로드 후)
- 동작 확인 ✓ (게임 내 ModConfig 탭에 노출됨)

## 사용자 피드백 누적
- "촌스럽다" (procedural PNG) → 게임 자산 활용 결정
- "구체 슬롯 → 별 슬롯" 컨셉 → 후에 ring + sparkle 으로 변경됨
- "구체 크기에서 점점 커지는 ripple" → expanding ripple 채택
- "발현 시점에만 모션, 이후 정적" → idle_loop 자동 전환 제거 시도
- "아무 애니메이션도 안 보임" → 최종 롤백 결정
