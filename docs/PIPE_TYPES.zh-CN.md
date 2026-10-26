# 管道类型与外观扩展

Fluid Pipes 0.7.0 提供 `FluidPipes.API.PipeTypes`。扩展通过自己的 `ItemProto` 注册程序化连接类型；默认安装包含管道和阀门。0.8.0 的 `PipeApi.DisableBuiltInItems()` 可在依赖插件的 `Awake` 中关闭内置内容，供扩展采用自有物品体系。

## API 边界

公共 API 随 `FluidPipes.dll` 一起发布，命名空间为 `FluidPipes.API`。扩展在编译时引用这个程序集、游戏程序集和所需 Unity 程序集，在运行时通过 BepInEx 前置依赖加载。源码按职责组织，类型的命名空间和程序集身份保持稳定。

| 入口或配置 | 职责 |
| --- | --- |
| `PipeApi`、`NetworkInventory` | 物品准入、共享库存读写和默认物品标识 |
| `PipeTypes`、`PipeTypeInfo` | 类型注册、身份查询、连通关系和变化通知 |
| `PipeTypeDefinition` | 绑定物品原型、运行行为、方向和连接家族 |
| `PipeAppearance`、`PipeMaterialSet`、`PipeEffectContext` | 材质初始化、表面角色和绘制参数 |
| `PipeGeometryProfile`、`PipeInteriorProfile`、`PipeInteriorSection` | 不可变的几何尺寸和结构截面 |

扩展面向这些公开类型实现功能。注册索引、Harmony 补丁、库存缓存、网格生成和材质缓存由库内部维护。`Structure` 为外部模拟提供连接与显示，`SharedInventory` 继续使用管道的库存规则；独立物品、配方和科技由扩展注册。

## 类型定义

每个类型分别指定运行行为、连接方向、外观和几何尺寸。结构类型可复用相同建造流程，由扩展通过拓扑 API 驱动供电、轨道或其它运行逻辑。

| 维度 | 选项 | 契约 |
| --- | --- | --- |
| `PipeBehavior` | `SharedInventory` | 连通节点参与共享库存，每节点容量 10；连接方向为 `Undirected` |
| `PipeBehavior` | `Structure` | 库提供建造、拓扑查询和渲染；运行行为由扩展维护，库存 API 返回失败 |
| `PipeDirectionMode` | `Undirected` | 对称连接，任意分支，建造预览使用格点 |
| `PipeDirectionMode` | `Directed` | 原生连接槽保存输入和输出，原生 `CargoPath` 维护方向与闭环；沿用原生传送带的输出数及路径约束 |
| `PipeAppearance` | 材质和效果回调 | 管壁、接头、内芯和机器接口的材质；区块级动画参数；连续纵向纹理坐标 |
| `PipeGeometryProfile` | 管径、中心高度和管箍尺寸 | 按类型生成管壁、接头与可选支撑环，单位为 Unity 世界单位 |
| `PipeInteriorProfile` | 纵向金属梁和导体截面 | 为结构类型配置沿实际路径延伸的圆形或圆角矩形构件 |

连接两端的 `Behavior`、`DirectionMode` 和 `ConnectionFamily` 必须一致。共享库存类型的默认家族为 `fluidpipes:inventory`；结构类型默认使用自己的完整类型键。扩展可指定相同家族，使不同外观的同类组件互连。

有向类型的方向以原生连接槽为准。无向拓扑缓存为有向类型提供弱连通查询；有向路径顺序通过输入、输出查询读取。同一连通空间的共享库存要求对称可达，因此 `SharedInventory` 与 `Directed` 的组合在定义构造时报告参数错误。外观和尺寸由各自的配置对象定义，方向策略与库存行为使用独立参数。

```csharp
var definition = new PipeTypeDefinition(
    localName: "signal-conduit",
    prototype: signalConduitItem,
    appearance: appearance,
    behavior: PipeBehavior.Structure,
    connectionFamily: "example.transport:signal",
    batchGroup: GetCircuitGroup,
    directionMode: PipeDirectionMode.Directed,
    geometry: new PipeGeometryProfile(
        radius: 0.45f,
        centerHeight: 0.48f,
        fittingScale: 1f,
        supportRingWidth: 0
    )
);
PipeTypes.Register("example.transport", definition);
```

类型键为 `ownerGuid + ":" + localName`。局部名称使用小写 ASCII 字母、数字、点、下划线或连字符。`PipeTypeInfo` 返回键、所属插件、实际物品 ID、行为、方向和连接家族的值快照。

## 原型注册

扩展声明 `[BepInDependency(PipeApi.PluginGuid, "0.7.0")]`。库的 `Awake` 先订阅 LDBTool，扩展随后订阅自己的原型处理器。

项目引用的 `FluidPipes.dll` 使用 `<Private>false</Private>`，发布包携带扩展自己的 DLL 和资源；运行时由已安装的 Fluid Pipes 提供公共程序集。完整独立项目见 [CopperPipes](../examples/CopperPipes/CopperPipes.csproj)。

1. 在 `LDBTool.PreAddDataAction` 中调用 `PipeTypes.CreateItem` 创建物品草稿，注册名称、图标、配方和科技。
2. 调用 `PipeTypes.Register` 绑定草稿和类型设置。已有 overhaul 也可以在自己的 `PostAddDataAction` 中绑定实际数据库原型。
3. `VFPreload.done` 后，库校验原型引用、最终 ID 和原生传送带组件，统一建立运行时索引并设置 `PipeApi.IsReady`。
4. 工厂查询和库存调用等待 `PipeApi.IsReady`。类型注册在数据库封闭后固定，生命周期覆盖整个游戏进程。

注册操作要求 Unity 主线程。重复键、重复物品归属、非法参数和封闭后的注册通过异常报告。最终原型校验失败时，库保持未就绪，并记录初始化错误。`IsPipeItem` 和 `TryGet` 可在注册阶段查询已收集的定义。

`CreateItem` 复制基础模板及数组字段，独立设置 ID、名称、描述和配方索引；模型原型与 `PrefabDesc` 仍由游戏数据库共享。模板在库的 pre-add 回调中准备，生命周期独立于内置物品是否入库，因此关闭内置内容后仍可创建外部物品。扩展通过 `PipeAppearance` 定制渲染。物品 ID、背包位置和配方 ID 的冲突管理属于扩展的原型注册流程。

关闭内置内容时，默认类型键 `com.komonad.dsp.fluidpipes:default` 保持未注册，`PipeApi` 的四个内置物品和配方 ID 属性返回 0；库完成外部类型索引和物品策略初始化后，`IsReady` 照常变为 `true`。初始化时序、进程级设置及存档内容要求见 [选择内置内容](API.zh-CN.md#选择内置内容)。

现有名称、配方、图标、科技和建造栏可以保留。独立物品示例见 [CopperPipes](../examples/CopperPipes/CopperPipesPlugin.cs)。

## 拓扑与外部运行逻辑

| API | 返回内容 |
| --- | --- |
| `GetNeighbors(factory, entityId)` | 已完成节点的直接相邻节点 |
| `GetIncomingNeighbors` | 有向输入节点；无向类型返回全部邻居 |
| `GetOutgoingNeighbors` | 有向输出节点；无向类型返回全部邻居 |
| `GetConnectedNodes` | 弱连通分量的节点，耗时与分量大小成正比 |
| `ConnectionsChanged` | 主线程后续更新中合并派发的连接变化通知 |

这些方法返回独立数组。调用方在主线程或持有工厂独占访问权时读取。实体 ID 可在拆除后复用，缓存需要随工厂切换和拓扑变化更新。

原生连接操作可以互相嵌套。通知在后续更新阶段派发，使观察者读取完整的连接状态；同一节点的重复变更合并。处理器适合标记受影响区域，在扩展自己的更新中完成一次重算。外部处理器异常记录诊断并与其它订阅者隔离。

扩展可按逻辑区域合并连接通知，在更新阶段读取无向连通关系或有向路径。成环、科技、供电和其它运行状态由扩展维护。`Structure` 类型的原生输送入口及库存 API 受类型边界保护。

## 几何尺寸

`PipeTypeDefinition.geometry` 接收不可变的 `PipeGeometryProfile`，省略时使用 `PipeGeometryProfile.Default`。

| 构造参数 | 默认值 | 有效范围 | 含义 |
| --- | ---: | --- | --- |
| `radius` | 0.32 | 0.08–1.2 | 外管壁半径；外径为该值的两倍 |
| `centerHeight` | 0.34 | 0–2 | 从原生实体位置沿行星径向到管道中心线的偏移 |
| `fittingScale` | 1 | 0.75–1.5 | 管箍厚度和轴向深度倍率；内孔随管壁半径适配 |
| `supportRingWidth` | 0 | 0–`radius` | 直段及端点中部支撑环的轴向宽度；零表示省略支撑环 |

构造器检查所有尺寸为有限值并处于有效范围。`centerHeight` 与原生实体位置共同确定渲染中心线。管壁、连接几何、选择轮廓和包围范围使用对应类型的尺寸。共享库存节点的容量由库存规则确定。

尺寸可表达结构梁、电缆护套和加宽管道。扩展应根据完整接头外径和中心偏移检查建筑间距、上下层净空及曲线连接。自定义吸附和放置规则由原型所属扩展维护。

## 内部纵向构件

`PipeAppearance.interior` 接收不可变的 `PipeInteriorProfile`，适用于 `PipeBehavior.Structure`。配置截面后，`HasCore` 自动为 `true`。省略 `interior` 时，`hasCore` 和 `coreRadiusFraction` 继续控制单个同轴内芯。`PipeWallMode.Open` 用于带有内部截面的结构类型，只绘制可见构件；选择描边同时覆盖金属梁和导体，碰撞保留 `PipeGeometryProfile` 的外部包络。

`new PipeInteriorProfile(members)` 捕获 1–16 个 `PipeInteriorMember` 的独立集合快照。`Members` 是只读列表，成员的 `Offset`、`Section` 和 `Surface` 在构造后固定。`RadiusFraction` 返回截面的包围半径比例。

| 成员参数 | 契约 |
| --- | --- |
| `offset` | 有限的 `Vector2`，相对于外管壁半径；端面 X 沿路径切线与行星径向上方向的叉积，Y 沿径向上方向在截面内的投影 |
| `PipeInteriorSection.Circle(radiusFraction)` | 圆形半径与外管壁半径之比，范围 `[0.01, 0.85]` |
| `PipeInteriorSection.RoundedRectangle(widthFraction, heightFraction, bevelFraction)` | 矩形全宽、全高各为 `[0.02, 1.7]`；圆角半径为 `[0, min(width, height) / 2]`，均相对于外管壁半径 |
| 截面包络 | `offset.magnitude + section.RadiusFraction < 0.9`；矩形采用半对角线作为保守包围半径 |
| `surface` | `Core` 使用导体材质和效果；`Fittings` 使用金属接头材质和效果 |

例如，外部包络半径为 1.0 的开敞结构可配置两侧箱梁、底板和中央导体：

```csharp
var beam = PipeInteriorSection.RoundedRectangle(0.24f, 0.72f, 0.04f);
var deck = PipeInteriorSection.RoundedRectangle(0.98f, 0.08f, 0.02f);
var interior = new PipeInteriorProfile(new[]
{
    new PipeInteriorMember(new Vector2(-0.5f, 0), beam, PipeSurfaceRole.Fittings),
    new PipeInteriorMember(new Vector2(0.5f, 0), beam, PipeSurfaceRole.Fittings),
    new PipeInteriorMember(new Vector2(0, -0.35f), deck, PipeSurfaceRole.Fittings),
    new PipeInteriorMember(new Vector2(0, 0.1f), 0.12f, PipeSurfaceRole.Core)
});

var appearance = new PipeAppearance(wallMode: PipeWallMode.Open, interior: interior);
var definition = new PipeTypeDefinition(
    "structural-beam",
    structuralBeamItem,
    appearance,
    PipeBehavior.Structure,
    geometry: new PipeGeometryProfile(radius: 1.0f, centerHeight: 1.08f)
);
PipeTypes.Register("example.transport", definition);
```

`Directed` 类型允许非对称截面，构件沿原生输入到输出的方向排列。`Undirected` 类型要求 `(x, y)` 与 `(-x, y)` 成员一一匹配，截面种类、全宽、全高、圆角及表面角色均相同；重复成员也需要对应数量的镜像，X 为零的成员与自身匹配。路径反向会翻转横向 X，径向 Y 保持朝外，因此单独底板和左右箱梁可以组成上下有区别的 U 形截面。构造类型定义时校验这一约束，共享端面按相同框架对接。

内构件沿真实中心线路径生成，偏移使用共享端面的切线与径向框架，球面相邻管段的截面位置保持连续。分支节点为每个连接臂生成一段构件。几何随节点形状缓存，并按类型及语义区块合并；拓扑或类型变化使相关缓存失效。动画与供电效果通过材质参数更新。内部网格的子网格 0 使用 `Core`，子网格 1 使用 `Fittings`。

圆角矩形构件沿路径扫掠，圆角与平面侧壁共同构成纵梁，独立端点使用平面封口。开敞截面可通过两侧箱梁、底板与导体组合保留中央通道。各类结构使用自身外半径缩放同一份归一化截面。

## 材质、纹理与动画

`PipeAppearance` 在构造时固定以下设置：

- `wallMode`：`Glass` 使用透明内外壁，`Opaque` 使用写入深度的实体管壁；`Open` 绘制带内部截面的结构类型。
- `configureMaterials`：每轮资源初始化时配置库创建的材质副本。
- `hasCore`、`coreRadiusFraction`：结构类型的装饰内芯，半径比例范围为 `(0, 0.85]`。
- `interior`：结构类型的多构件截面；配置后使用各成员的偏移、半径和表面角色生成内部几何。
- `applyEffects`：每次可见区块绘制时更新 `MaterialPropertyBlock`。
- `textureCoordinate`：纯函数，将世界空间中心线位置映射到纵向 UV；默认采用局部弧长。
- `textureCoordinatePeriod`：周期坐标映射的重复间隔，零表示连续坐标。经度映射可指定 512，使网格在跨越经度接缝时展开相邻 UV；重复采样的纹理使用整数周期。

```csharp
var appearance = new PipeAppearance(
    wallMode: PipeWallMode.Glass,
    configureMaterials: materials =>
    {
        materials.Core.color = new Color(0.12f, 0.02f, 0.30f);
        materials.Core.EnableKeyword("_EMISSION");
        materials.Core.SetTexture("_EmissionMap", beamTexture);
    },
    hasCore: true,
    coreRadiusFraction: 0.40f,
    applyEffects: (context, properties) =>
    {
        if (context.Surface == PipeSurfaceRole.Core)
        {
            properties.SetColor("_EmissionColor", new Color(0.6f, 0.2f, 1f) * 3f);
            properties.SetVector("_MainTex_ST", new Vector4(1, 1, 0, -context.TimeSeconds));
        }
    },
    textureCoordinate: position => Mathf.Atan2(position.z, position.x) / (2 * Mathf.PI) * 512,
    textureCoordinatePeriod: 512
);
```

`PipeMaterialSet` 提供 `Wall`、`Fittings`、`Core`、`MachinePanel` 和 `MachineShell`。`SetMaterial` 将自有 Shader 的材质复制到角色。`InnerWall` 在配置结束后从管壁派生。自有 Shader 应支持相应渲染类别和普通网格顶点、法线及 UV。

库创建和销毁材质副本；共享的纹理、Shader 和 AssetBundle 由扩展保持有效。`SetMaterial` 的源材质归调用方所有，完成复制后可释放临时源材质。材质引用的纹理和 Shader 继续覆盖整个绘制生命周期，扩展在最后一次绘制结束后释放这些资源。配置异常或 Shader 校验失败时释放该次副本，使用默认材质并记录诊断。绘制回调异常按类型隔离。

动画参数写入借用的 `MaterialPropertyBlock`，有效期为本次回调。材质配置、纹理生成和网格生成位于初始化或拓扑变化阶段。逐帧回调适合读取缓存状态并设置少量参数。`GetConnectedNodes` 会分配数组并遍历分量，适合拓扑变更后的重算阶段。

使用 Standard Shader 时，移动发光纹理可通过主 UV 变换驱动，面板接缝与细磨砂纹理可放在独立的 detail UV 通道。这样逐帧只需设置发光颜色及 UV 偏移，固定表面细节保持其几何位置。纹理生成完成后可调用 `Apply(updateMipmaps: true, makeNoLongerReadable: true)` 释放 CPU 像素副本。

循环结构可使用周期坐标保持接缝连续。例如，对于中心线整圈长度为 `C`、目标纹理周期为 `L` 的水平圆环，扩展可取重复数 `N = max(1, round(C / L))`，经度映射为 `atan2(z, x) / (2π) * P`，并设置 `textureCoordinatePeriod: P`。库按最近相位展开相邻截面坐标；扩展把纹理纵向缩放设为 `N / P`，实际世界周期为 `C / N`。同一圆环的所有空间区块应使用相同的 `C`、`N` 和 `P`。动画世界速度可除以 `C / N` 换算为每秒纹理周期数。

环向 U 按截面实际周长归一化。圆形采用均匀角度；圆角矩形按直边长度和圆角弧长累计，平面与圆角具有相同纹理密度。接缝位置复制为 U = 0 和 U = 1，端帽使用独立平面映射。

绘制按空间区块、物品类型和 `batchGroup` 分组。该纯函数在节点几何放置时计算稳定语义组，例如电路编号。`PipeEffectContext` 包含工厂、代表节点、语义组、时间和表面角色；同一批次内的节点共享本次参数。需要不同运行状态的组件应划入不同语义组，逐帧参数查询可由组键直接定位扩展维护的状态。

几何配置覆盖管壁、管箍及支撑环的尺寸；表面配置覆盖材质、纹理、装饰内芯与 Shader 效果。拓扑与库存配置保持各自的运行语义。

## 存档与兼容

运行时存档格式为 6，读取格式 1–6。原生存档保存物品 ID、建筑、待建对象和有向连接槽；Mod 数据保存无向连接、共享库存、阀门和机器接口。渲染资源由注册定义重建。

扩展负责维持物品 ID 和类型语义稳定，并在加载相应存档时安装所需依赖。当前格式通过原生物品 ID 恢复类型；跨 ID 迁移需要同时覆盖原生建筑、背包、待建对象和 Mod 数据。独立的类型归属清单属于后续存档格式扩展。

有向节点在读档后从原生槽恢复弱连通查询，无向节点从保存的连接恢复。历史默认管段的超容量库存继续保留并允许提取。类型注册和公共 API 保持 API v1 的现有签名。

`PipeAppearance` 保留以下七参数构造签名，供已编译的消费者 DLL 链接。该重载的参数均为必填项，并委托到 `interior: null` 的构造路径。

```csharp
public PipeAppearance(
    PipeWallMode wallMode,
    Action<PipeMaterialSet> configureMaterials,
    bool hasCore,
    float coreRadiusFraction,
    Action<PipeEffectContext, MaterialPropertyBlock> applyEffects,
    Func<Vector3, float> textureCoordinate,
    float textureCoordinatePeriod
);
```

八参数重载在末尾提供可选的 `PipeInteriorProfile interior = null`，所有参数继续支持默认值。反射契约测试检查七参数签名及其参数可选性，源码调用可通过 `interior:` 命名参数选用内部截面。

圆形成员的三参数构造 `PipeInteriorMember(Vector2, float, PipeSurfaceRole)` 同样保留二进制签名。截面构造重载为 `PipeInteriorMember(Vector2, PipeInteriorSection, PipeSurfaceRole)`，圆形构造委托到 `PipeInteriorSection.Circle`。

`PipeTypeDefinition` 保留七参数签名 `(string, ItemProto, PipeAppearance, PipeBehavior, string, Func<PlanetFactory, int, int>, PipeDirectionMode)`，全部参数必填，并委托到 `geometry: null` 的八参数构造路径。八参数重载允许通过可选 `geometry` 设置尺寸。
