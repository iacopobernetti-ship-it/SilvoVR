# LCC SDK API Reference

> 文档基线：`dev_v20` 分支 HEAD（commit `fea7060`）。本次刷新覆盖了
> `SetRenderLayer`、`SetForceRefresh`、`SetMipMode` 新增方法，
> `Renderer.IsRenderAll` 属性、`Renderer.SetRenderAll` 详细说明，
> `SetRaycastDelta` 范围修正（[0.1, 10.0]），以及 `MipMode` 枚举补充。

## 目录

- [1. LCCManager 类](#1-lccmanager-类)
- [2. Renderer 类](#2-renderer-类)
- [3. 附录：关键数据类型](#3-附录关键数据类型)

---

## 1. LCCManager 类

**命名空间：** LCCCore

**继承：** MonoBehaviour

**描述：** LCC SDK 的核心管理器，负责管理所有 Renderer 实例、相机、全局渲染状态、编辑操作（裁切/高亮/调色）、交互查询（射线/碰撞/吸附）等。整个场景中应仅存在一个 LCCManager 实例。


### 1.0 总览

- **属性列表**
  - 无公开属性。所有功能通过方法调用。

- **方法列表**

  **前置操作项（初始化与配置）**
  - SetPlatformType(PlatformType _type) → 设置目标平台
  - SwitchRenderPass(ActiveRenderMode _mode) → 设置渲染模式（MultiRender / SingleRender）
  - GetRender(Transform _transform) → 创建渲染实例（绑定到已有Transform）
  - GetRender(out GameObject gameObject) → 创建渲染实例（自动创建GameObject）
  - SetMaxBufferSplat(int _count) → 设置GPU最大Buffer容量（一次性分配）
  - SetFullRenderSplat(int _count) → 设置 PC/Mac SingleRender Full Load 模式下全量渲染上限
  - SetMaxRenderSplats(int _count) → 设置运行时最大渲染Splat数量


  **过程中操作API（运行时控制）**
  - SetDetailLevel(float sse) → 设置渲染精度等级 [1,100]
  - SetStartLod(int lod) → 设置LOD起始层级（0-based，0=最精细）
  - SetEndLod(int lod) → 已废弃 (`[Obsolete]`)，调用即直接 return
  - SetSelectionMode(SelectionMode mode) → 节点筛选策略 Auto/Normal/Large
  - SetAutoFov(bool _auto) → 设置自动FOV
  - SetFOV(int, int, float, float) → 手动设置FOV参数
  - SetShadowReceive(bool enable) → 开关阴影接收
  - SetShadowColor(Color color) → 设置阴影颜色
  - SetShadowStrength(float strength) → 设置阴影强度
  - SetLights(List<PointLightData> lights) → 设置自定义点光源（返回 0/-1）
  - SetRecordMode(bool, Vector2, float) → 设置录制模式
  - SetRenderEnable(Renderer, bool) → 控制指定Renderer渲染开关
  - SetEnvironment(Renderer, bool) → 控制指定Renderer环境数据渲染
  - SetZDepth(bool zwrite) → 开关Z深度写入
  - SetAlpha(float alpha) → 设置全局透明度 [0,1]
  - SetLightIntensity(float intensity) → 设置光照强度 [0,1]
  - SetLockFPS(bool _isLock) → 开关帧率锁定
  - SetRenderLayer(int _layer) → 设置Unity渲染层级 [0,31]
  - SetForceRefresh() → 强制刷新（清除相机缓存）
  - SetMipMode(MipMode _mipMode) → 切换Mip模式（Mip/Non_Mip）
  - SetGraphicsAPI(GraphicsDeviceType type) → 设置图形API类型（DX11 启用 HDR）
  - SetDebugMode(bool debug) → 开关调试模式
  - SetEditorMode(bool _isEditor) → 开关编辑器模式
  - SetAECMode(bool _aec) → 开关AEC模式
  - SetSemantic(bool enable, Vector4[] color) → 开关语义渲染（颜色数组长度必须为100）
  - SwitchRenderMode(RenderMode mode, Texture2D tex) → 切换渲染模式（点云/3DGS）
  - SetRaycastDelta(float delta) → 设置射线检测精度 [0.1, 10.0]
  - SetMaxRaycastDistance(float dist) → 设置最大射线检测距离 [10, 10000]
  - SetMainCamera(Camera _cam) → 设置主渲染相机
  - AddCamera(Camera _cam) → 添加副相机
  - RemoveCamera(Camera _cam) → 移除副相机

  **裁切/高亮/调色API**
  - SetClip(Vector3, Vector3, bool) → 平面裁切
  - SetClip(List<Data2D>) → 2D纹理裁切
  - SetClip(List<Data3D>) → 3D几何体裁切
  - SetClip(List<DataMix>) → 混合模式裁切
  - QuitClipMode() → 退出裁切模式
  - SetHighlight(List<Data2D>) → 2D纹理高亮
  - SetHighlight(List<Data3D>) → 3D几何体高亮
  - SetHighlight(List<DataMix>) → 混合模式高亮
  - QuitHighlightMode() → 退出高亮模式
  - SetTone(List<ToneMix>) → 调色操作
  - QuitToneMode() → 退出调色模式


  **吸附功能API**
  - SetSnapEnabled(bool enable) → 开关吸附功能
  - SetSnapRadius(float pixels) → 设置吸附半径
  - SetEdgeThreshold(float threshold) → 设置边缘检测阈值
  - SetSnapPreviewEnabled(bool enable) → 开关吸附预览
  - UpdateSnapPreview(Vector2, Camera) → 更新吸附预览状态
  - GetSnapPreviewState() → 获取吸附预览状态

  **查询与交互API**
  - Raycast(Vector3, out HitResult) → 屏幕坐标射线检测
  - Raycast(Ray, out HitResult) → 自定义射线检测
  - RaycastMesh(Vector3, out HitResult, float) → 屏幕坐标网格射线检测
  - RaycastMesh(Ray, out HitResult, out Bounds, float) → 自定义射线网格检测
  - RaycastWithSnap(Vector2, Camera, out HitResult) → 带吸附的射线检测
  - IntersectsSphere(Sphere) → 球体碰撞检测
  - IntersectsSphere(Sphere, out Vector3) → 球体碰撞检测（含穿透向量）
  - IntersectsCapsule(Capsule) → 胶囊体碰撞检测
  - IntersectsCapsule(Capsule, out Vector3) → 胶囊体碰撞检测（含穿透向量）

  **VFX**
  - TriggerVFX(Vector3, int) → 触发VFX特效

  **查询**
  - GetAllRenderers() → 获取所有Renderer实例


---

### 1.1 属性项

无公开属性。所有功能通过方法调用。

---

### 1.2 方法项

#### 1.2.1 前置操作项

**SetPlatformType() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetPlatformType
(
    PlatformType _type   // 目标平台类型
)
```
- 功能说明：设置SDK目标运行平台。必须在任何 Renderer 创建（GetRender）之前调用，影响内部渲染策略和资源分配。
- 调用示例：
```csharp
lccManager.SetPlatformType(PlatformType.PC);
```
- 注意事项：
  - 必须在任何 GetRender 调用之前执行
  - 可选值：PC, Mac, Android, iOS, Pico, Quest, AVP


---

**SwitchRenderPass() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SwitchRenderPass
(
    ActiveRenderMode _mode   // 渲染模式
)
```
- 功能说明：设置全局渲染架构模式。`MultiRender` 使用共享 GPU Buffer + 单 DrawCall，可同时渲染最多 255 个 Renderer；`SingleRender` 由 SDK 内部根据数据量自动选择 Full Load（独立 GPU Buffer）或 Chunk（内部仍走 RenderCore，但用户契约限制单 Renderer）。
- 调用示例：
```csharp
lccManager.SwitchRenderPass(ActiveRenderMode.MultiRender);
```
- 注意事项：
  - 必须在任何 Renderer.Load() 之前调用
  - 仅 PC/Mac 平台有效（移动端/VR 自动使用 MobileLOD）
  - MultiRender + HTTP 数据源会被 `Renderer.Load` 拒绝；HTTP 数据请使用 SingleRender（内部自动走 chunk 路径）
  - 模式锁定后（任意 Renderer.Load 调用过）不能再切换；只有 SingleRender chunk 路径下最后一个 Renderer 被 Dispose 时才会解锁


---

**GetRender() 方法（重载1：绑定Transform）**
- 方法原型：
```csharp
// 返回值：Renderer → 渲染实例，失败返回 null
public Renderer GetRender
(
    Transform _transform   // 挂载的 Transform
)
```
- 功能说明：创建一个新的 LCC 渲染实例并绑定到指定 Transform。创建后需调用 Renderer.Load() 加载数据。
- 调用示例：
```csharp
var go = new GameObject("LCCScene");
var renderer = lccManager.GetRender(go.transform);
renderer.Load(filePath, () => { Debug.Log("Loaded"); });
```
- 注意事项：
  - 需先调用 SetPlatformType
  - 需先设置主相机 SetMainCamera
  - MultiRender 模式下最多支持 255 个 Renderer


---


**SetMaxBufferSplat() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetMaxBufferSplat
(
    int _count   // 最大Splat数量，单位：万
)
```
- 功能说明：设置 GPU Buffer 最大容量（Splat数量）。在 RenderCore 路径（MultiRender / SingleRender chunk）下决定共享 Global Buffer 大小；在 SingleRender Full Load 模式下会按数据量实际分配，不参考此值。**一次性分配**，不可动态修改。
- 调用示例：
```csharp
lccManager.SetMaxBufferSplat(3000); // 3000万点（PC/Mac默认值）
lccManager.SetMaxBufferSplat(4500); // 4500万点（PC/Mac最大值）
```
- 注意事项：
  - 单位为"万"，传入值 × 10000 = 实际Splat数
  - 必须在任何 Renderer.Load() 之前调用，否则报错并 return
  - 必须先调用 SetPlatformType
  - 容量越大显存占用越高，按需设置
  - 平台上限：PC/Mac 4500万（`Config.c_maxBufferSplatCeiling`），Android/iOS 600万，VR 200万
  - 默认值：PC/Mac 3000万，Android/iOS 300万，VR 100万

---

**SetFullRenderSplat() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetFullRenderSplat
(
    int _count   // 全量渲染上限，单位：万
)
```
- 功能说明：设置 PC/Mac SingleRender Full Load 模式下的全量渲染上限阈值（写入 `Config.c_maxFullRenderingSplatNum`）。当数据 LOD0 的 splat 数小于此值时走 Full Load 路径（独立 GPU Buffer），否则走 Chunk 路径（内部 RenderCore）。
- 调用示例：
```csharp
lccManager.SetFullRenderSplat(2500); // 2500万点
```
- 注意事项：
  - 单位为"万"
  - 仅 PC/Mac 平台支持，其他平台调用直接报错并 return
  - 必须在任何 Renderer.Load() 之前调用
  - 必须先调用 SetPlatformType
  - 取值范围 [100, 4500]（即 100万 ~ 4500万）


---


#### 1.2.2 过程中操作API

**SetDetailLevel() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetDetailLevel
(
    float sse   // 精度等级 [1-100]
)
```
- 功能说明：设置渲染精度等级。使用对数映射将 [1, 100] 映射到内部精度参数 `Config.c_sseUp`（区间 [150, 8]）：1=最粗 sseUp=150，100=最精细 sseUp=8。值越大渲染越精细、性能消耗越高。
- 调用示例：
```csharp
lccManager.SetDetailLevel(50f); // 中等精度
```
- 注意事项：
  - 范围 [1, 100]，超出自动钳制
  - 对数映射：低值区间变化敏感，高值区间变化平缓
  - 运行时可动态调整
  - 仅在 Normal 选择模式下生效；Large 模式以预算驱动，SSE 仅作排序键


---

**SetStartLod() / SetEndLod() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetStartLod
(
    int lod   // LOD层级 [0-10]，0=最精细
)
[Obsolete]
public void SetEndLod
(
    int lod   // 当前实现中已废弃，调用即直接 return
)
```
- 功能说明：`SetStartLod` 设置 LOD 展开起点（0-based）。`0` 表示最精细，`1` 为次精细，依此类推；数字越大层级越粗。`SetEndLod` 已被标记为 `[Obsolete]`，方法体已注释为 return，调用不会产生任何效果（当前 SDK 通过自动 LOD 终止策略代替）。
- 调用示例：
```csharp
lccManager.SetStartLod(0);
// SetEndLod 调用即 no-op
```
- 注意事项：
  - SetStartLod 范围 [0, 10]，超出自动钳制
  - 实际生效上限受数据 totalLevels 限制
  - 调用 `SetStartLod` 后，`m_renderCore != null` 时 SDK 会请求 selector 立即重排一次


---

**SetAutoFov() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetAutoFov
(
    bool _auto   // 是否自动FOV
)
```
- 功能说明：设置是否自动根据相机参数计算FOV。开启后SDK自动从Camera组件读取FOV参数。
- 调用示例：
```csharp
lccManager.SetAutoFov(true);
```
- 注意事项：
  - 默认开启
  - 关闭后需手动调用 SetFOV 设置参数


---

**SetFOV() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetFOV
(
    int _width,        // 像素宽度
    int _height,       // 像素高度
    float _verticalFov, // 垂直FOV（度）
    float _aspect       // 宽高比
)
```
- 功能说明：手动设置渲染FOV参数。用于非标准相机或录制模式下的自定义分辨率。
- 调用示例：
```csharp
lccManager.SetFOV(1920, 1080, 60f, 1.778f);
```
- 注意事项：
  - 需先调用 SetAutoFov(false)
  - 录制模式下自动使用此参数


---

**SetShadowReceive() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetShadowReceive
(
    bool enable   // 是否接收阴影
)
```
- 功能说明：开关点云阴影接收功能。开启后 Mesh 物体的阴影可投射到 3DGS 点云上。
- 调用示例：
```csharp
lccManager.SetShadowReceive(true);
```
- 注意事项：
  - 默认关闭
  - 需要场景中有投射阴影的 Mesh 和 Directional Light
  - 启用 _SPLAT_SHADOW_ON shader keyword
  - Shadow Buffer 初始化时必须填充 1.0f


---

**SetShadowColor() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetShadowColor
(
    Color color   // 阴影颜色
)
```
- 功能说明：设置阴影区域的颜色。
- 调用示例：
```csharp
lccManager.SetShadowColor(new Color(0.3f, 0.3f, 0.3f, 1f));
```
- 注意事项：
  - 默认值 (0.3, 0.3, 0.3, 1)
  - 需先开启 SetShadowReceive(true)


---

**SetShadowStrength() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetShadowStrength
(
    float strength   // 阴影强度 [0-1]
)
```
- 功能说明：设置阴影强度。0为无阴影，1为最强阴影。
- 调用示例：
```csharp
lccManager.SetShadowStrength(0.8f);
```
- 注意事项：
  - 默认值 1.0
  - 范围 [0, 1]，超出自动钳制
  - 需先开启 SetShadowReceive(true)


---

**SetLights() 方法**
- 方法原型：
```csharp
// 返回值：int → 0=成功, -1=超出最大数量限制
public int SetLights
(
    List<PointLightData> lights   // 点光源列表
)
```
- 功能说明：设置自定义点光源列表。传入 null 或空列表可关闭自定义光照。
- 调用示例：
```csharp
var lights = new List<PointLightData>();
lights.Add(new PointLightData {
    position = new Vector4(0, 5, 0, 1),
    color = new Vector4(1, 1, 1, 1),
    range = 10f,
    intensity = 2f
});
lccManager.SetLights(lights);
```
- 注意事项：
  - 最大数量受 Config.c_maxLightCount 限制
  - 传入 null 或空列表关闭自定义光照
  - 启用 _USERLIGHT shader keyword


---

**SetRecordMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetRecordMode
(
    bool isRecord,       // 是否录制模式
    Vector2 renderSize,  // 录制分辨率
    float verticalFov    // 垂直FOV
)
```
- 功能说明：设置录制模式。录制模式下使用固定分辨率和FOV渲染，适用于高清视频录制。
- 调用示例：
```csharp
lccManager.SetRecordMode(true, new Vector2(3840, 2160), 60f);
```
- 注意事项：
  - 仅 PC/Mac 平台支持
  - 录制模式下相机焦距固定，不随Camera组件变化
  - 结束录制后调用 SetRecordMode(false, ...) 恢复


---

**SetRenderEnable() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetRenderEnable
(
    Renderer _renderer,   // 目标渲染实例
    bool _isEnable        // 是否启用渲染
)
```
- 功能说明：在运行时控制指定 Renderer 的渲染开关。禁用后不参与遍历和绘制调用。
- 调用示例：
```csharp
lccManager.SetRenderEnable(renderer, false); // 隐藏
lccManager.SetRenderEnable(renderer, true);  // 显示
```


---

**SetEnvironment() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetEnvironment
(
    Renderer _renderer,   // 目标渲染实例
    bool _isRender        // 是否渲染环境数据
)
```
- 功能说明：控制指定 Renderer 的环境数据渲染。环境数据为低精度背景点云，用于填充远景。
- 调用示例：
```csharp
lccManager.SetEnvironment(renderer, true);
```
- 注意事项：
  - StreamingRender 模式下默认关闭
  - 数据需包含环境节点


---

**SetZDepth() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetZDepth
(
    bool zwrite   // 是否开启Z深度写入
)
```
- 功能说明：开关Z深度写入，同时调整渲染队列。
- 调用示例：
```csharp
lccManager.SetZDepth(true);
```


---

**SetAlpha() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetAlpha
(
    float alpha   // 全局透明度 [0-1]
)
```
- 功能说明：设置全局透明度。
- 调用示例：
```csharp
lccManager.SetAlpha(0.8f);
```
- 注意事项：
  - 范围 [0, 1]，超出自动钳制


---

**SetLightIntensity() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetLightIntensity
(
    float intensity   // 光照强度 [0-1]
)
```
- 功能说明：设置全局光照强度。
- 调用示例：
```csharp
lccManager.SetLightIntensity(0.7f);
```
- 注意事项：
  - 范围 [0, 1]，超出自动钳制


---

**SetRaycastDelta() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetRaycastDelta
(
    float delta   // 射线检测精度 [0.1-10.0]
)
```
- 功能说明：设置射线检测的精度参数。值越小精度越高但性能消耗越大。
- 调用示例：
```csharp
lccManager.SetRaycastDelta(0.5f);
```
- 注意事项：
  - 默认值 0.1
  - 范围 [0.1, 10.0]，超出自动钳制
  - 影响所有 Raycast 方法的检测精度


---
**SetMainCamera() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetMainCamera
(
    Camera _cam   // 主渲染相机，传 null 可清除
)
```
- 功能说明：设置主渲染相机。LCC 使用此相机进行视锥裁剪、LOD选择、排序等操作。切换相机时通过协程延迟到帧末执行，确保帧安全。
- 调用示例：
```csharp
lccManager.SetMainCamera(Camera.main);
```
- 注意事项：
  - 必须在 Renderer.Load() 之前设置
  - 切换相机时重新调用即可
  - 仅 PC/Mac 平台支持
  - 传 null 可清除主相机


---

**AddCamera() 方法**
- 方法原型：
```csharp
// 返回值：void
public void AddCamera
(
    Camera _cam   // 副相机
)
```
- 功能说明：添加一个副相机。副相机参与渲染但不影响LOD选择。
- 调用示例：
```csharp
lccManager.AddCamera(secondCamera);
```
- 注意事项：
  - 仅 PC/Mac 平台支持
  - 不能将主相机添加为副相机


---

**RemoveCamera() 方法**
- 方法原型：
```csharp
// 返回值：void
public void RemoveCamera
(
    Camera _cam   // 要移除的副相机
)
```
- 功能说明：移除一个已添加的副相机。
- 调用示例：
```csharp
lccManager.RemoveCamera(secondCamera);
```
- 注意事项：
  - 仅 PC/Mac 平台支持


---

**GetRender() 方法（重载2：自动创建GameObject）**
- 方法原型：
```csharp
// 返回值：Renderer → 渲染实例，失败返回 null
public Renderer GetRender
(
    out GameObject gameObject   // 输出创建的GameObject
)
```
- 功能说明：自动创建名为 "LCCRenderer" 的 GameObject，并将 Renderer 绑定到其 Transform 上。
- 调用示例：
```csharp
var renderer = lccManager.GetRender(out GameObject go);
renderer.Load(filePath, () => { Debug.Log("Loaded"); });
```
- 注意事项：
  - 失败时 gameObject 输出为 null
  - 其余同重载1


---


**SetLockFPS() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetLockFPS
(
    bool _isLock   // 是否锁定帧率
)
```
- 功能说明：开关帧率锁定（静态/动态帧率切换）。
- 调用示例：
```csharp
lccManager.SetLockFPS(true);
```


---

**SetRenderLayer() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetRenderLayer
(
    int _layer   // Unity渲染层级 [0-31]
)
```
- 功能说明：设置 LCC 渲染使用的 Unity Layer。影响 `Config.c_renderLayer`，用于相机 CullingMask 控制可见性。
- 调用示例：
```csharp
lccManager.SetRenderLayer(8); // 使用 Layer 8
```
- 注意事项：
  - 范围 [0, 31]，超出范围 LogError 并 return
  - 运行时可动态调整


---

**SetForceRefresh() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetForceRefresh()
```
- 功能说明：强制刷新渲染。清除内部相机信息缓存，使下一帧重新计算视锥、LOD 等参数。适用于外部修改相机参数后需要立即生效的场景。
- 调用示例：
```csharp
lccManager.SetForceRefresh();
```


---

**SetMipMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetMipMode
(
    MipMode _mipMode   // Mip 或 Non_Mip
)
```
- 功能说明：切换 Mip 模式。`Mip` 模式使用 mipmap 采样（默认），`Non_Mip` 模式禁用 mipmap（启用 `_NONMip` shader keyword）。
- 调用示例：
```csharp
lccManager.SetMipMode(MipMode.Non_Mip); // 禁用 mipmap
lccManager.SetMipMode(MipMode.Mip);     // 恢复 mipmap
```
- 注意事项：
  - 若当前模式与传入相同则直接 return（无重复开销）
  - 切换立即生效（全局 shader keyword）


---

**SetGraphicsAPI() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetGraphicsAPI
(
    GraphicsDeviceType type   // 图形API类型
)
```
- 功能说明：设置图形API类型。DX11 模式下启用 HDR。
- 调用示例：
```csharp
lccManager.SetGraphicsAPI(GraphicsDeviceType.Direct3D11);
```


---

**SetDebugMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetDebugMode
(
    bool debug   // 是否开启调试模式
)
```
- 功能说明：开关调试模式。
- 调用示例：
```csharp
lccManager.SetDebugMode(true);
```
- 注意事项：
  - 仅 PC/Mac 平台支持


---

**SetEditorMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetEditorMode
(
    bool _isEditor   // 是否开启编辑器模式
)
```
- 功能说明：开关编辑器模式。
- 调用示例：
```csharp
lccManager.SetEditorMode(true);
```
- 注意事项：
  - 仅 PC/Mac 平台支持


---

**SetAECMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetAECMode
(
    bool _aec   // 是否开启AEC模式
)
```
- 功能说明：开关AEC（建筑/工程/施工）模式。AEC模式下裁切使用 _CLIPAEC keyword。
- 调用示例：
```csharp
lccManager.SetAECMode(true);
```
- 注意事项：
  - 仅 PC/Mac 平台支持


---

**SetSemantic() 方法**
- 方法原型：
```csharp
// 返回值：bool → false=参数无效
public bool SetSemantic
(
    bool enable,              // 是否启用语义渲染
    Vector4[] color = null    // 语义颜色数组（长度必须为100）
)
```
- 功能说明：开关语义渲染模式。启用时可传入颜色数组定义各语义类别的显示颜色。
- 调用示例：
```csharp
Vector4[] colors = new Vector4[100];
colors[0] = new Vector4(1, 0, 0, 1); // 类别0=红色
lccManager.SetSemantic(true, colors);
```
- 注意事项：
  - color 数组长度必须为 100，否则返回 false
  - 传 null 使用默认颜色


---

**SwitchRenderMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SwitchRenderMode
(
    RenderMode mode,         // 渲染模式
    Texture2D tex = null     // 高程色带图（可选）
)
```
- 功能说明：切换点云渲染与 3DGS 渲染模式。可提供高程色带纹理用于点云模式。
- 调用示例：
```csharp
lccManager.SwitchRenderMode(RenderMode.PointCloud, elevationTex);
```
- 注意事项：
  - 仅 PC/Mac 平台支持


---

**TriggerVFX() 方法**
- 方法原型：
```csharp
// 返回值：void
public void TriggerVFX
(
    Vector3 vfxOriginW,   // VFX 世界坐标原点
    int vfxid = 0         // VFX效果ID
)
```
- 功能说明：在指定世界坐标触发VFX特效。切换 shader keyword 到 _VFXMIX。
- 调用示例：
```csharp
lccManager.TriggerVFX(hitPoint, 0);
```


---

**GetAllRenderers() 方法**
- 方法原型：
```csharp
// 返回值：List<Renderer> → 所有已注册的Renderer实例副本
public List<Renderer> GetAllRenderers()
```
- 功能说明：获取当前所有已注册的 Renderer 实例列表（返回副本）。
- 调用示例：
```csharp
var renderers = lccManager.GetAllRenderers();
foreach (var r in renderers) { /* ... */ }
```


---

**SetMaxRenderSplats() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetMaxRenderSplats
(
    int _count   // 运行时最大渲染Splat数量，单位：万
)
```
- 功能说明：设置运行时每帧最大渲染Splat数量。可在 Load() 之后动态调用，受 SetMaxBufferSplat 设置的上限钳制。SingleRender Full Load 模式下调用无效（LogWarning 后直接 return）。
- 调用示例：
```csharp
lccManager.SetMaxRenderSplats(2500); // 2500万点
```
- 注意事项：
  - 单位为"万"，传入值 × 10000 = 实际Splat数
  - 可在运行时动态调整
  - 不能超过 SetMaxBufferSplat 设置的容量上限（超出自动钳制 + LogWarning）
  - SingleRender Full Load 模式（数据量 < `c_maxFullRenderingSplatNum`）不支持运行时调整
  - RenderCore 路径下转发到 `GlobalNodeSelector` 的 tile 预算；MobileLOD 路径下逐 Renderer 转发到 `PCGlobalTraversal.SetMaxSplatNum`

---

**SetSelectionMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetSelectionMode
(
    SelectionMode mode   // Auto / Normal / Large
)
```
- 功能说明：设置节点筛选模式。`Auto` 默认，根据所有 Renderer 的世界空间 union AABB 自动决策（最大维度超过 `Config.c_largeSceneThreshold` 默认 380m 时**单向**升级到 Large，不会回退）。`Normal` 适用于小中场景，使用桶 + SSE 阈值流程。`Large` 适用于超大场景（1000m+），改用预算驱动贪心 + sse/delta 优先级。
- 调用示例：
```csharp
lccManager.SetSelectionMode(SelectionMode.Auto);
lccManager.SetSelectionMode(SelectionMode.Large); // 强制 Large
```
- 注意事项：
  - 仅在 RenderCore 路径下生效（MultiRender / SingleRender chunk）；MobileLOD 不影响
  - 切换模式立即生效，丢弃当前 selection snapshot 重新筛选下一 cycle
  - Auto 模式下一旦升级到 Large，除非用户显式 `SetSelectionMode(Auto)` + 场景 union AABB 缩到阈值以下（实际上 Auto 不会回退；下次显式 `Normal` 才能强制覆盖）


---

#### 1.2.3 裁切/高亮/调色API

**SetClip() 方法（平面裁切）**
- 方法原型：
```csharp
// 返回值：int → 0=成功
public int SetClip
(
    Vector3 planePoint,    // 平面上一点
    Vector3 planeNormal,   // 平面法线
    bool inside = true     // true=保留法线方向侧
)
```
- 功能说明：使用无限平面裁切点云。保留法线方向一侧或反侧的点。
- 调用示例：
```csharp
lccManager.SetClip(Vector3.zero, Vector3.up, true); // 保留地面以上
```
- 注意事项：
  - 启用 _CLIPPLANE shader keyword
  - 与其他裁切模式互斥


---

**SetClip() 方法（2D纹理裁切）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量
public int SetClip
(
    List<Data2D> _2Ds   // 2D纹理选区列表
)
```
- 功能说明：使用2D投影纹理蒙版裁切点云。通过 MVP 矩阵将纹理投影到3D空间定义裁切区域。
- 调用示例：
```csharp
var clips = new List<Data2D>();
clips.Add(new Data2D {
    texture = maskTex,
    mv = modelViewMatrix,
    mvp = mvpMatrix,
    mode = SelectMode.normal
});
lccManager.SetClip(clips);
```
- 注意事项：
  - 纹理格式 R8 1024x1024
  - mode: normal=保留区域内, invert=保留区域外
  - 启用 _CLIP2D shader keyword


---

**SetClip() 方法（3D几何体裁切）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量, 3=有效几何体为0
public int SetClip
(
    List<Data3D> _3Ds   // 3D几何体列表
)
```
- 功能说明：使用3D几何体（Box/Sphere/Cylinder等）裁切点云。支持布尔运算组合多个几何体。
- 调用示例：
```csharp
var clips = new List<Data3D>();
clips.Add(new Data3D {
    type = 1,
    invMat = boxInverseMatrix,
    operation = OperationMode.normal,
    mode = SelectMode.normal
});
lccManager.SetClip(clips);
```
- 注意事项：
  - invMat 为几何体逆变换矩阵
  - operation: normal=并集, subtract=差集
  - 至少需要一个 operation==normal 的几何体
  - AEC模式下启用 _CLIPAEC keyword，否则启用 _CLIP3D keyword


---

**SetClip() 方法（混合模式裁切）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量
public int SetClip
(
    List<DataMix> mix   // 混合选区列表（2D+3D）
)
```
- 功能说明：同时使用2D纹理和3D几何体混合定义裁切区域。每个条目通过 editType 指定类型。
- 调用示例：
```csharp
var mix = new List<DataMix>();
mix.Add(new DataMix {
    editType = EditType.Edit3D,
    type = 1,
    invMat = matrix,
    operation = OperationMode.normal,
    mode = SelectMode.normal
});
lccManager.SetClip(mix);
```
- 注意事项：
  - 启用 _CMIX shader keyword
  - 2D和3D条目可任意组合


---

**QuitClipMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void QuitClipMode()
```
- 功能说明：退出裁切模式，恢复完整点云显示。清除所有裁切相关的 shader keyword 和 GPU Buffer。
- 调用示例：
```csharp
lccManager.QuitClipMode();
```
- 注意事项：
  - 同时清除 _CLIPPLANE/_CLIP2D/_CLIP3D/_CMIX/_CLIPAEC/_CNULL 所有裁切 keyword


---

**SetHighlight() 方法（2D纹理高亮）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量
public int SetHighlight
(
    List<Data2D> _2Ds   // 2D纹理选区列表
)
```
- 功能说明：使用2D投影纹理蒙版对点云进行高亮显示。高亮区域外的点云变暗。
- 调用示例：
```csharp
var highlights = new List<Data2D>();
highlights.Add(new Data2D {
    texture = maskTex,
    mv = modelViewMatrix,
    mvp = mvpMatrix,
    mode = SelectMode.normal
});
lccManager.SetHighlight(highlights);
```
- 注意事项：
  - 启用 _H2D shader keyword
  - 与3D高亮和Mix高亮互斥
  - 纹理格式 R8 1024x1024


---

**SetHighlight() 方法（3D几何体高亮）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量, 3=有效几何体为0
public int SetHighlight
(
    List<Data3D> _3Ds   // 3D几何体列表
)
```
- 功能说明：使用3D几何体定义高亮区域。支持布尔运算组合。
- 调用示例：
```csharp
var highlights = new List<Data3D>();
highlights.Add(new Data3D {
    type = 1,
    invMat = boxInverseMatrix,
    operation = OperationMode.normal,
    mode = SelectMode.normal
});
lccManager.SetHighlight(highlights);
```
- 注意事项：
  - 启用 _H3D shader keyword
  - invMat 为几何体逆变换矩阵
  - 至少需要一个 operation==normal 的几何体


---

**SetHighlight() 方法（混合模式高亮）**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量
public int SetHighlight
(
    List<DataMix> mix   // 混合选区列表
)
```
- 功能说明：同时使用2D纹理和3D几何体混合定义高亮区域。
- 调用示例：
```csharp
var mix = new List<DataMix>();
mix.Add(new DataMix {
    editType = EditType.Edit3D,
    type = 1,
    invMat = matrix,
    operation = OperationMode.normal,
    mode = SelectMode.normal
});
lccManager.SetHighlight(mix);
```
- 注意事项：
  - 启用 _HMIX shader keyword
  - 2D和3D条目可任意组合


---

**QuitHighlightMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void QuitHighlightMode()
```
- 功能说明：退出高亮模式，恢复点云正常显示。清除所有高亮相关的 shader keyword 和 GPU Buffer。
- 调用示例：
```csharp
lccManager.QuitHighlightMode();
```
- 注意事项：
  - 同时清除 _H2D/_H3D/_HMIX/_HNULL 所有高亮 keyword


---

**SetTone() 方法**
- 方法原型：
```csharp
// 返回值：int → 0=成功, 1=列表为空, 2=超出最大数量
public int SetTone
(
    List<ToneMix> mix   // 调色混合列表
)
```
- 功能说明：对选定区域进行调色处理。支持亮度、饱和度、对比度调整和LUT滤镜。可通过2D纹理或3D几何体定义调色区域。
- 调用示例：
```csharp
var tones = new List<ToneMix>();
tones.Add(new ToneMix {
    editType = EditType.Edit3D,
    type = 1,
    invMat = boxMatrix,
    operation = OperationMode.normal,
    mode = SelectMode.normal,
    brightness = 1.2f,
    saturation = 1.0f,
    contrast = 1.1f,
    lutid = 0
});
lccManager.SetTone(tones);
```
- 注意事项：
  - 启用 _TONE shader keyword
  - brightness/saturation/contrast 默认值为 1.0
  - lutid 为 LUT 滤镜索引，0 表示不使用
  - 2D和3D条目可混合使用


---

**QuitToneMode() 方法**
- 方法原型：
```csharp
// 返回值：void
public void QuitToneMode()
```
- 功能说明：退出调色模式，恢复点云原始色彩。清除调色相关的 shader keyword 和 GPU Buffer。
- 调用示例：
```csharp
lccManager.QuitToneMode();
```
- 注意事项：
  - 清除 _TONE keyword 并释放 TMixBuffer


---

#### 1.2.4 吸附功能API

**SetSnapEnabled() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetSnapEnabled
(
    bool enable   // 是否启用吸附
)
```
- 功能说明：开关吸附功能。启用后 RaycastWithSnap 会自动搜索附近的边缘/角点进行吸附。
- 调用示例：
```csharp
lccManager.SetSnapEnabled(true);
```
- 注意事项：
  - 默认关闭
  - 仅 PC/Mac 平台支持，移动端/VR 调用无效并输出警告
  - 启用后会分配 GPU 资源（EdgeDetect/CornerDetect/SnapSearch compute shader）


---

**SetSnapRadius() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetSnapRadius
(
    float pixels   // 吸附搜索半径（像素）
)
```
- 功能说明：设置吸附搜索半径。在此像素范围内搜索最近的边缘/角点。
- 调用示例：
```csharp
lccManager.SetSnapRadius(30f);
```
- 注意事项：
  - 默认 20 像素
  - 值越大搜索范围越广，但可能吸附到非预期目标
  - 仅 PC/Mac 平台支持


---

**SetEdgeThreshold() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetEdgeThreshold
(
    float threshold   // 边缘检测阈值
)
```
- 功能说明：设置 Sobel 边缘检测的阈值。值越小检测到的边缘越多。
- 调用示例：
```csharp
lccManager.SetEdgeThreshold(0.2f);
```
- 注意事项：
  - 默认 0.3
  - 范围建议 [0.1, 0.8]
  - 值过小会产生大量噪声边缘
  - 仅 PC/Mac 平台支持


---

**SetSnapPreviewEnabled() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetSnapPreviewEnabled
(
    bool enable   // 是否启用吸附预览
)
```
- 功能说明：开关吸附预览功能。启用后鼠标移动时实时显示最近的吸附目标指示器。
- 调用示例：
```csharp
lccManager.SetSnapPreviewEnabled(true);
```
- 注意事项：
  - 需先调用 SetSnapEnabled(true)
  - 仅 PC/Mac 平台支持
  - 预览会持续消耗 GPU 资源（每帧检测）


---

**UpdateSnapPreview() 方法**
- 方法原型：
```csharp
// 返回值：void
public void UpdateSnapPreview
(
    Vector2 mousePos,   // 当前鼠标屏幕坐标
    Camera cam          // 相机
)
```
- 功能说明：更新吸附预览状态。在鼠标移动时调用，更新预览指示器位置。
- 调用示例：
```csharp
void Update() {
    lccManager.UpdateSnapPreview(Input.mousePosition, Camera.main);
}
```
- 注意事项：
  - 需先调用 SetSnapPreviewEnabled(true)
  - 建议在 Update 中持续调用
  - 仅 PC/Mac 平台支持


---

**GetSnapPreviewState() 方法**
- 方法原型：
```csharp
// 返回值：SnapPreviewState → 吸附预览状态（screenPos 是屏幕坐标）
public SnapPreviewState GetSnapPreviewState()
```
- 功能说明：获取当前吸附预览状态，包括是否有吸附目标、目标类型、目标屏幕位置和到鼠标的像素距离。
- 调用示例：
```csharp
var state = lccManager.GetSnapPreviewState();
if (state.hasTarget)
{
    // state.snapType: Corner / Edge
    // state.screenPos: 吸附目标屏幕坐标
    // state.distanceToMouse: 像素距离
}
```
- 注意事项：
  - 需先调用 SetSnapPreviewEnabled(true) 和 UpdateSnapPreview
  - 仅 PC/Mac 平台支持
  - 无目标时返回 default(SnapPreviewState)
  - **`screenPos` 是屏幕坐标，不是世界坐标**；要拿世界坐标请用 RaycastWithSnap 取 `HitResult.hitPos`


---

#### 1.2.5 查询与交互API

**Raycast() 方法（屏幕坐标）**
- 方法原型：
```csharp
// 返回值：bool → true=命中
public bool Raycast
(
    Vector3 mousePos,              // 屏幕坐标
    out HitResult _finalResult     // 命中结果
)
```
- 功能说明：从屏幕坐标发射射线检测点云命中。使用 SetMainCamera 设置的主相机生成射线。
- 调用示例：
```csharp
if (lccManager.Raycast(Input.mousePosition, out HitResult hit))
{
    Debug.Log(hit.hitPos); // 注意字段名为 hitPos
}
```
- 注意事项：
  - 需先调用 SetMainCamera 设置主相机
  - 返回距离相机最近的命中点
  - 精度受 SetRaycastDelta / SetMaxRaycastDistance 影响
  - 该重载没有 _maxDistance 参数，默认范围由 `Config.c_maxRaycastDistance` 控制（可用 SetMaxRaycastDistance 调整）

---

**Raycast() 方法（Ray）**
- 方法原型：
```csharp
// 返回值：bool → true=命中
public bool Raycast
(
    Ray _ray,                      // 射线
    out HitResult _finalResult     // 命中结果
)
```
- 功能说明：使用自定义 Ray 进行射线检测。遍历所有激活的 Renderer 返回最近命中。
- 调用示例：
```csharp
Ray ray = new Ray(origin, direction);
if (lccManager.Raycast(ray, out HitResult hit))
{
    Debug.Log(hit.hitPos);
}
```
- 注意事项：
  - 适用于自定义射线（非相机发射）
  - 遍历所有激活的 Renderer 实例
  - 距离上限由 `Config.c_maxRaycastDistance` 控制


---

**RaycastMesh() 方法（屏幕坐标）**
- 方法原型：
```csharp
// 返回值：bool → true=命中
public bool RaycastMesh
(
    Vector3 mousePos,             // 屏幕坐标
    out HitResult _finalResult,   // 命中结果
    float _disatnce = 70.0f       // 最大检测距离
)
```
- 功能说明：从屏幕坐标发射射线检测点云碰撞网格。使用 Collider 数据进行检测，比 Raycast 更快但精度较低。
- 调用示例：
```csharp
if (lccManager.RaycastMesh(Input.mousePosition, out HitResult hit))
{
    Debug.Log(hit.position);
}
```
- 注意事项：
  - 需要数据包含 Collider 节点
  - 需先调用 Renderer.SetColliderEnable(true)


---

**RaycastMesh() 方法（Ray + Bounds）**
- 方法原型：
```csharp
// 返回值：bool → true=命中
public bool RaycastMesh
(
    Ray ray,                 // 射线
    out HitResult result,    // 命中结果
    out Bounds hitBounds,    // 命中节点的包围盒
    float maxDist = 70.0f    // 最大检测距离
)
```
- 功能说明：使用自定义 Ray 进行碰撞网格检测，同时返回命中节点的包围盒信息。
- 调用示例：
```csharp
Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
if (lccManager.RaycastMesh(ray, out HitResult hit, out Bounds bounds))
{
    Debug.Log(bounds.center);
}
```
- 注意事项：
  - 需要数据包含 Collider 节点
  - hitBounds 为命中的 LOD 节点包围盒


---

**RaycastWithSnap() 方法**
- 方法原型：
```csharp
// 返回值：bool → true=命中
public bool RaycastWithSnap
(
    Vector2 screenPos,       // 屏幕坐标
    Camera cam,              // 相机
    out HitResult result     // 命中结果（含吸附信息）
)
```
- 功能说明：带吸附功能的射线检测。先进行普通 Raycast，再在命中点附近搜索边缘/角点进行吸附。
- 调用示例：
```csharp
if (lccManager.RaycastWithSnap(mousePos, cam, out HitResult hit))
{
    // hit.snapType: Corner/Edge/None
    // hit.hitPos: 吸附后的世界坐标（实际字段名为 hitPos，不是 position）
    // hit.originalHitPos: 吸附前的原始命中点
}
```
- 注意事项：
  - 需先调用 SetSnapEnabled(true)
  - 仅 PC/Mac 平台支持，移动端/VR 不可用（直接 LogWarning + return false）
  - 优先级：Corner > Edge > None
  - **本入口同步阻塞主线程**：使用 `AsyncGPUReadback.WaitForCompletion()` 等待 16 字节结果
  - 预览模式（`SetSnapPreviewEnabled(true)`）下若 `m_SnapManager.CurrentState.hasTarget` 为真，则直接复用预览缓存，不再重新 dispatch；否则走正常吸附流程


---

**IntersectsSphere() 方法**
- 方法原型：
```csharp
// 返回值：bool → true=相交
public bool IntersectsSphere
(
    Sphere sphere   // 球体
)

// 返回值：bool → true=相交
public bool IntersectsSphere
(
    Sphere sphere,       // 球体
    out Vector3 delta    // 穿透向量（推出方向和距离）
)
```
- 功能说明：检测球体与点云碰撞体是否相交。第二个重载额外返回穿透向量，可用于物理推出。
- 调用示例：
```csharp
var sphere = new Sphere(transform.position, 0.5f);
if (lccManager.IntersectsSphere(sphere, out Vector3 delta))
{
    transform.position += delta;
}
```
- 注意事项：
  - 需要数据包含 Collider 节点
  - 需先调用 Renderer.SetColliderEnable(true)
  - 遍历所有激活的 Renderer


---

**IntersectsCapsule() 方法**
- 方法原型：
```csharp
// 返回值：bool → true=相交
public bool IntersectsCapsule
(
    Capsule capsule   // 胶囊体
)

// 返回值：bool → true=相交
public bool IntersectsCapsule
(
    Capsule capsule,     // 胶囊体
    out Vector3 delta    // 穿透向量
)
```
- 功能说明：检测胶囊体与点云碰撞体是否相交。常用于角色控制器碰撞检测。第二个重载返回穿透向量。
- 调用示例：
```csharp
var capsule = new Capsule(footPos, headPos, 0.3f);
if (lccManager.IntersectsCapsule(capsule, out Vector3 delta))
{
    characterController.Move(delta);
}
```
- 注意事项：
  - 需要数据包含 Collider 节点
  - 需先调用 Renderer.SetColliderEnable(true)
  - 适合第一人称/第三人称角色碰撞


---

## 2. Renderer 类

**命名空间：** LCCCore

**描述：** LCC 渲染实例，负责单个 3DGS 场景的数据加载、渲染控制。通过 LCCManager.GetRender() 创建，不可直接实例化。

### 2.0 总览

- **属性列表**
  - Transform : Transform → 渲染实例挂载的 Transform（只读）
  - IsEnable : bool → 渲染实例是否激活（只读）
  - IsRenderAll : bool → 是否全量渲染 LOD0 数据（只读）

- **方法列表**
  - Load(string _filePath, Action&lt;float&gt; _onProgress, Action _onComplete) → 加载 3DGS 数据
  - Unload() → 释放数据，保留实例供再次 Load
  - Dispose() → 销毁渲染实例
  - SetEnable(bool _enable) → 设置激活状态
  - SetColliderEnable(bool _enable) → 开关碰撞体
  - SetEnvironment(bool _env) → 开关环境数据渲染
  - SetRenderAll(bool _renderAll) → PC/Mac SingleRender 下开关全量渲染（下次 Load 生效）
  - GetBounds(out Vector3 max, out Vector3 min) → 获取包围盒
  - GetSourceType() → 获取数据来源类型
  - GetDataType() → 获取数据格式类型
  - GetMeta(string _filePath, out SplatMeta _metaData) → 从文件路径获取元数据
  - GetMeta(out SplatMeta _metaData) → 从已加载数据获取元数据


---

### 2.1 属性项

- **Transform**
  - 类型：Transform
  - 功能说明：渲染实例挂载的 Transform 组件
  - 只读

- **IsEnable**
  - 类型：bool
  - 功能说明：渲染实例是否处于激活状态
  - 默认值：true
  - 只读

- **IsRenderAll**
  - 类型：bool
  - 功能说明：是否正在全量渲染 LOD0 数据（vs. 流式/LOD 遍历模式）
  - 默认值：false
  - 只读


---

### 2.2 方法项

#### 2.2.1 前置操作项

**Load() 方法**
- 方法原型：
```csharp
// 返回值：void
public void Load
(
    string _filePath,             // 数据文件路径（本地路径或HTTP URL）
    Action<float> _onProgress,    // 加载进度回调（可为 null）
    Action _onComplete            // 加载完成回调（可为 null）
)
```
- 功能说明：加载 3DGS 数据文件。仅支持 `.lcc2` 后缀；支持本地文件和 HTTP 远程文件。
  - **进度回调** `_onProgress`：
    - SingleRender Full Load 路径：在 `Renderer.Update` 中按已加载节点比例上报，钳制到 [0, 0.99]
    - SingleRender Chunk / MultiRender / HTTP / MobileLOD：仅在首次 GPU 数据可见时同步触发一次 `_onProgress(1.0f)`
  - **完成回调** `_onComplete`：在数据真正落 GPU 后触发，与 `_onProgress(1.0f)` 同帧
- 调用示例：
```csharp
renderer.Load("D:/data/scene.lcc2",
    p => Debug.Log($"Loading {p:P0}"),
    () => {
        Debug.Log("Load complete");
        lccManager.SetShadowReceive(true);
    });
```
- 注意事项：
  - MultiRender 模式不允许 HTTP 数据源（构造期 LogError + return）
  - 必须先完成 `SetPlatformType` / `SwitchRenderPass` / `SetMainCamera` 等前置配置
  - HTTP 仅支持 `.lcc2`；其他后缀直接报错
  - 进度回调可能在后台线程入栈但实际 Invoke 一律在主线程，已捕获回调内异常并 LogError
  - **重新 Load 必须先 `Unload()` 或 `Dispose()`**，直接重 Load 同一对象会触发 RenderCore 内部 `NotifyRendererReload` 走完整重置流程


---

**Dispose() 方法**
- 方法原型：
```csharp
// 返回值：void
public void Dispose()
```
- 功能说明：销毁渲染实例，释放所有 GPU 资源和内存。销毁后实例不可再使用，所有公共方法因 `m_isDisposed` 防御自动 no-op。
- 调用示例：
```csharp
renderer.Dispose();
renderer = null;
```
- 注意事项：
  - 调用后自动通知 LCCManager 移除引用并归还 matrixIndex
  - RenderCore 路径下会触发 GPU Tile 回收
  - SingleRender chunk 路径下，最后一个 Renderer Dispose 时会拆除 RenderCore 并解锁 `m_modeLocked`

---

**Unload() 方法**
- 方法原型：
```csharp
// 返回值：void
public void Unload()
```
- 功能说明：释放当前已加载数据，回到 "已创建未加载" 状态，**保留 matrixIndex 和管理器注册**。可反复 `Load → Unload → Load` 循环复用同一个 Renderer 实例，避免重复申请矩阵索引和加载 shader 资源。
- 调用示例：
```csharp
renderer.Unload();
renderer.Load(newFilePath, null, () => Debug.Log("Reloaded"));
```
- 注意事项：
  - 与 Dispose 的差异：不移除 `m_renderers` 注册、不归还 matrixIndex、不解锁 `m_modeLocked`、不拆除 RenderCore
  - Unload 后 `IsEnable = false`、`m_loadCompleted = false`，再次 Load 会重置所有数据状态
  - 仅在 `m_loadCompleted == true` 时执行；未加载或已 Dispose 时直接 return


---


#### 2.2.2 过程中操作API

**SetEnable() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetEnable
(
    bool _enable   // 是否激活
)
```
- 功能说明：设置渲染实例的激活状态。禁用后不参与渲染和 LOD 选择。
- 调用示例：
```csharp
renderer.SetEnable(false); // 隐藏
renderer.SetEnable(true);  // 显示
```
- 注意事项：
  - StreamingRender 模式下禁用会触发 GPU Tile 驱逐
  - 不释放资源，可随时重新激活


---

**SetRenderAll() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetRenderAll
(
    bool _renderAll   // 是否全量渲染
)
```
- 功能说明：设置是否使用 Full Load 模式（全量渲染 LOD0 数据）。仅 PC/Mac SingleRender 模式下有效。设置后在**下次 Load() 调用时**生效，不影响当前已加载的数据集。
- 调用示例：
```csharp
renderer.SetRenderAll(true);
renderer.Load(filePath, null, () => Debug.Log("Full loaded"));
```
- 注意事项：
  - 仅 PC/Mac 平台支持，移动端/VR 调用输出 LogWarning 并 return
  - 如果数据已加载完成，设置仍可成功但会输出 LogWarning 提示需要重新 Load 才生效
  - 配合 `IsRenderAll` 属性查询当前状态


---

**SetColliderEnable() 方法**
- 方法原型：
```csharp
// 返回值：bool → true=设置成功, false=数据不含碰撞体或实例未激活
public bool SetColliderEnable
(
    bool _enable   // 是否启用碰撞体
)
```
- 功能说明：开关碰撞体功能。启用后 IntersectsSphere/IntersectsCapsule 和 RaycastMesh 可用。
- 调用示例：
```csharp
bool ok = renderer.SetColliderEnable(true);
if (!ok) Debug.LogWarning("数据不含碰撞体");
```
- 注意事项：
  - 数据必须包含 Collider 节点，否则返回 false
  - 需要 Renderer 处于激活状态


---

**SetEnvironment() 方法**
- 方法原型：
```csharp
// 返回值：void
public void SetEnvironment
(
    bool _env   // 是否渲染环境数据
)
```
- 功能说明：开关环境数据渲染。环境数据为低精度背景点云，用于填充远景。
- 调用示例：
```csharp
renderer.SetEnvironment(true);
```
- 注意事项：
  - 数据需包含环境节点
  - StreamingRender 模式下默认关闭
  - 环境数据独立于主数据的 LOD 系统


---

**GetBounds() 方法**
- 方法原型：
```csharp
// 返回值：void
public void GetBounds
(
    out Vector3 max,   // 包围盒最大点（世界坐标）
    out Vector3 min    // 包围盒最小点（世界坐标）
)
```
- 功能说明：获取渲染数据的轴对齐包围盒（世界坐标）。
- 调用示例：
```csharp
renderer.GetBounds(out Vector3 max, out Vector3 min);
Vector3 center = (max + min) / 2f;
Vector3 size = max - min;
```
- 注意事项：
  - 数据未加载完成时返回 Vector3.zero
  - 包围盒已经过 localToWorldMatrix 变换
  - 可用于相机定位和视角计算


---

**GetSourceType() 方法**
- 方法原型：
```csharp
// 返回值：SourceType → 数据来源类型
public SourceType GetSourceType()
```
- 功能说明：获取已加载数据的来源类型。
- 返回值：SourceType.LCC / PLY / SPLAT / None
- 注意事项：
  - 数据未加载时返回 SourceType.None


---

**GetDataType() 方法**
- 方法原型：
```csharp
// 返回值：DataType → 数据格式类型
public DataType GetDataType()
```
- 功能说明：获取已加载数据的格式类型。
- 返回值：DataType.L1 / L2 / L2Pro / K1 / Drone / PortalCam / None
- 注意事项：
  - 数据未加载时返回 DataType.None


---

**GetMeta() 方法（从文件路径）**
- 方法原型：
```csharp
// 返回值：bool → true=读取成功
public bool GetMeta
(
    string _filePath,          // 数据文件路径
    out SplatMeta _metaData    // 输出的元数据
)
```
- 功能说明：从指定文件路径读取 3DGS 数据的元信息，无需完整加载数据。
- 调用示例：
```csharp
if (renderer.GetMeta(filePath, out SplatMeta meta))
{
    Debug.Log(meta.source);
    Debug.Log(meta.dataType);
}
```
- 注意事项：
  - 支持本地路径和 HTTP URL
  - 仅读取文件头部元信息，不加载点云数据
  - 路径为空或文件不存在时返回 false


---

**GetMeta() 方法（从已加载数据）**
- 方法原型：
```csharp
// 返回值：bool → true=读取成功
public bool GetMeta
(
    out SplatMeta _metaData   // 输出的元数据
)
```
- 功能说明：获取当前已加载数据的元信息。若数据尚未加载完成，则尝试从文件路径读取。
- 调用示例：
```csharp
if (renderer.GetMeta(out SplatMeta meta))
{
    Debug.Log(meta.source);
}
```
- 注意事项：
  - 数据加载完成后直接返回内存中的 Meta
  - 数据未加载时回退到从文件路径读取


---

## 3. 附录：关键数据类型

### PlatformType
```csharp
public enum PlatformType { PC, Mac, Android, iOS, Pico, Quest, AVP }
```

### ActiveRenderMode
```csharp
public enum ActiveRenderMode { MultiRender, SingleRender, MobileLOD }
```

### SourceType
```csharp
public enum SourceType { None, LCC, PLY, SPLAT }
```

### DataType
```csharp
public enum DataType { None, L1, L2, L2Pro, K1, Drone, PortalCam }
```

### SnapType
```csharp
public enum SnapType { None, Edge, Corner }
```

### MipMode
```csharp
public enum MipMode { Mip, Non_Mip }
```


### HitResult
```csharp
public struct HitResult {
    public bool isHit;             // 是否命中
    public float distance;         // 当前实现初始化为 Config.c_raycastRange
    public Vector3 hitPos;         // 命中点世界坐标（实际字段名为 hitPos，非 position）
    public Vector3 normal;         // 命中法线
    public float hitAlpha;         // 被击中椭球的 Alpha
    public float dist2Origin;      // 到射线源的距离
    public float scale;            // 椭球 scale
    public SnapType snapType;      // 吸附类型 (None / Edge / Corner)
    public Vector3 originalHitPos; // 吸附前原始命中点
}
```
> ⚠️ **字段命名差异**：实际字段是 `hitPos` 而非 `position`，`dist2Origin` 而非 `distance` 才是排序使用的距离值；调用方代码请按实际字段名访问。

### SnapPreviewState
```csharp
public struct SnapPreviewState {
    public bool hasTarget;          // 是否有吸附目标
    public SnapType snapType;       // None / Edge / Corner
    public Vector2 screenPos;       // 吸附目标屏幕坐标（非世界坐标）
    public float distanceToMouse;   // 到鼠标位置的像素距离
}
```
> ⚠️ 当前实现的字段是 `screenPos`（屏幕像素），不是世界坐标。需要世界坐标时请用 `RaycastWithSnap` 取 `HitResult.hitPos`。
### SnapPreviewState 已上移至 HitResult 段

### Data2D
```csharp
public struct Data2D {
    public Texture2D texture;  // R8 1024x1024 蒙版纹理
    public Matrix4x4 mv;      // ModelView 矩阵
    public Matrix4x4 mvp;     // ModelViewProjection 矩阵
    public SelectMode mode;   // normal/invert
}
```

### Data3D
```csharp
public struct Data3D {
    public ShapeType type;          // box / sphere
    public Matrix4x4 invMat;       // 几何体逆变换矩阵
    public OperationMode operation; // normal=并集, erase=差集
    public SelectMode mode;         // normal / invert / clip
}
```
> ⚠️ `type` 在代码中是 `ShapeType` 枚举（box/sphere），不是 int；`OperationMode` 的差集枚举名是 `erase`，不是 `subtract`。

### DataMix
```csharp
public struct DataMix {
    public EditType editType;      // Edit2D / Edit3D
    public int type;               // 3D几何体类型
    public Matrix4x4 invMat;      // 3D逆变换矩阵
    public Texture2D texture;     // 2D蒙版纹理
    public Matrix4x4 mv;          // 2D ModelView
    public Matrix4x4 mvp;         // 2D MVP
    public OperationMode operation;
    public SelectMode mode;
}
```


### ToneMix
```csharp
public struct ToneMix {
    public EditType editType;      // Edit2D / Edit3D
    public int type;               // 3D几何体类型
    public Matrix4x4 invMat;      // 3D逆变换矩阵
    public Texture2D texture;     // 2D蒙版纹理
    public Matrix4x4 mv;          // 2D ModelView
    public Matrix4x4 mvp;         // 2D MVP
    public OperationMode operation;
    public SelectMode mode;
    public float brightness;       // 亮度 (默认1.0)
    public float saturation;       // 饱和度 (默认1.0)
    public float contrast;         // 对比度 (默认1.0)
    public int lutid;              // LUT滤镜索引 (0=不使用)
}
```


### PointLightData
```csharp
public struct PointLightData {
    public Vector4 position;   // xyz=位置, w=1
    public Vector4 color;      // rgba 颜色
    public float range;        // 光照范围
    public float intensity;    // 光照强度
}
```

### 枚举类型
```csharp
public enum SelectMode { normal, invert, clip }
public enum OperationMode { normal = 0, erase = 1 }
public enum EditType { Edit2D, Edit3D }
public enum RenderMode { PointCloud, LCCGS }
public enum ShapeType { box, sphere }
public enum SelectionMode { Auto, Normal, Large }
public enum ActiveRenderMode { MultiRender, SingleRender }
```