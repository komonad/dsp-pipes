# Fluid Pipes API 接口文档

Fluid Pipes 同时提供管道建筑和供其它 BepInEx mod 使用的共享库存 API。安装插件及基础依赖后，即可使用管道和阀门的建造、运输、渲染与交互功能。外部集成通过 `FluidPipes.API` 命名空间访问同一套运行逻辑，实现类型使用 `internal` 可见性。

独立管道类型的注册、外观和原型接入由 `PipeTypes` 提供，详见[管道类型与外观扩展](PIPE_TYPES.md)。[CopperPipes](../examples/CopperPipes/README.md) 是仅依赖公开 API 的独立插件，注册铜制管道物品、配方与材质。

## 添加依赖

目标框架为 .NET Framework 4.7.2。编译时引用 `FluidPipes.dll`、BepInEx 和游戏程序集，Fluid Pipes 引用设置 `Private=false`，由运行环境加载已安装的插件。`FluidPipes.xml` 放在 DLL 旁可提供 IDE 文档提示。

```xml
<Reference Include="FluidPipes">
  <HintPath>path/to/FluidPipes.dll</HintPath>
  <Private>false</Private>
</Reference>
```

```csharp
using BepInEx;
using FluidPipes.API;

[BepInPlugin("example.pipeconsumer", "Pipe Consumer", "1.0.0")]
[BepInDependency(PipeApi.PluginGuid, PipeApi.ModVersion)]
public sealed class ConsumerPlugin : BaseUnityPlugin
{
}
```

Fluid Pipes 的 GUID 是 `com.komonad.dsp.fluidpipes`。运行时需要安装 Fluid Pipes 及其依赖 BepInEx 5、CommonAPI、LDBTool、DSPModSave。仓库中的 [可编译示例](../examples/PipeConsumer/PipeConsumer.csproj) 展示依赖声明、注册生命周期和存取方式。

依赖声明中的 `PipeApi.ModVersion` 在编译时固定为所引用库的插件版本，供 BepInEx 检查最低依赖版本。

类型定义通过 `PipeTypeDefinition.CreateSharedInventory` 或 `CreateStructure` 创建。外观使用一个 `PipeAppearance` 构造器，支持工厂感知的纹理坐标和 `receiveShadows` 设置。

## 初始化和生命周期

`PipeTypes` 提供独立物品类型、连接方向、结构组件和渲染扩展，详见 [类型与外观扩展](PIPE_TYPES.md)。共享库存 API 适用于 `PipeBehavior.SharedInventory`；`Structure` 类型通过拓扑 API 接入外部运行逻辑。

在依赖 mod 的 `Awake` 中可调用 `RegisterTransportItems`，物品 ID 会在游戏数据库初始化时解析。注册参数数组被复制；相同物品可由多个 mod 独立注册。注册返回 `IDisposable`，在所属插件的 `OnDestroy` 中释放。每个注册和配置授权分别管理生命周期，物品在至少一项授权有效时保持准入。

注册、释放必须在 Unity 主线程执行。数据库建立前的物品注册在初始化时解析，因此新增物品应通过 LDBTool 的原型注册阶段加入数据库。初始化后，注册以数据库中已经存在的物品 ID 为准。

游戏原型加载完成后 `PipeApi.IsReady` 为 `true`；此前各建筑、配方 ID 属性返回 0，库存操作返回 `false`。依赖 mod 通过 `PipeApi.PipeItemId`、`ValveItemId`、`PipeRecipeId`、`ValveRecipeId` 获取实际 ID。禁用内置内容时，这四个属性持续返回 0，外部注册类型照常完成初始化。

API 通过工厂的 `entityPool` 定位目标，调用方传入正数的 **实体 ID (`EntityData.id`)**。实体拆除后 ID 可能复用；切换存档、拆除和重新建造后，调用方需要重新解析工厂与实体绑定。

## 选择内置内容

默认安装包含流体管道、阀门、配方、科技解锁和建造栏入口。采用自有物品体系的扩展可通过 `PipeApi.DisableBuiltInItems()` 在进程初始化时关闭这组内容：

```csharp
using BepInEx;
using FluidPipes.API;
using xiaoye97;

[BepInPlugin("example.custompipes", "Custom Pipes", "1.0.0")]
[BepInDependency(PipeApi.PluginGuid, PipeApi.ModVersion)]
public sealed class CustomPipesPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        PipeApi.DisableBuiltInItems();
        LDBTool.PreAddDataAction += RegisterItems;
    }

    private void RegisterItems()
    {
        var item = PipeTypes.CreateItem(9700, "CustomPipes.Name", "CustomPipes.Description");
        LDBTool.PreAddProto(item);
        PipeTypes.Register(
            "example.custompipes",
            PipeTypeDefinition.CreateSharedInventory("custom", item)
        );
    }

    private void OnDestroy()
    {
        LDBTool.PreAddDataAction -= RegisterItems;
    }
}
```

此调用要求 Unity 主线程，并位于 LDBTool 开始原型注册之前；依赖插件的 `Awake` 是对应的调用阶段。同一阶段可重复调用，关闭状态持续到进程结束。进入原型注册阶段后调用会抛出 `InvalidOperationException`，跨线程调用也使用该异常报告。

关闭后，库跳过内置物品、阀门模型、配方、科技解锁、建造栏和调试赠送物品；共享库存、物品准入、拓扑、建造和渲染 API 仍然可用。`PipeTypes.CreateItem` 使用库在 pre-add 阶段准备的独立模板，扩展继续注册自己的物品、配方及类型。

内容选择属于整个进程的配置，多个插件共享同一状态。每个存档应保持稳定的内容集合：含内置管道或阀门的存档继续启用内置内容；采用自有物品体系的存档保持相应扩展及其注册设置。存档中的实体和库存由已安装的对应物品定义解释。

## 类型与拓扑查询

`PipeTypeDefinition.CreateSharedInventory` 定义普通共享库存管道；`CreateStructure` 定义由扩展维护运行逻辑的连接结构。两者校验类型参数并返回可交给 `PipeTypes.Register` 的定义。`CreateStructure` 通过 `constructionMode:` 选择建造模式。已有原型可直接绑定，不必通过 `CreateItem` 重建名称、配方或科技。完整注册时序、参数和结构示例见 [类型与外观扩展](PIPE_TYPES.md)。

`PipeTypes.TryGet` 返回的 `PipeTypeInfo` 包含身份、连接策略、几何配置，以及只读的 `Appearance` 和 `ConstructionMode`。`Appearance` 是注册时的配置引用，允许为 `null`；它不提供绘制中的可变材质或属性块。

`PipeAppearance.textureCoordinate` 接收 `(PlanetFactory factory, Vector3 position)`，保证工厂非空，返回有限的纵向纹理坐标。省略回调时使用局部弧长；缺少工厂的内部预览直接使用局部弧长，不调用回调。异常、`NaN` 或无穷值会记录诊断并停用该类型的坐标回调。`receiveShadows` 默认 `true`，可在构造外观时设置，投影行为与受影设置独立。

| 查询 | 结果及成本 |
| --- | --- |
| `GetNeighbors`、`GetIncomingNeighbors`、`GetOutgoingNeighbors` | 独立的直接邻居数组；方向按类型定义解释 |
| `GetConnectedNodes` | 弱连通分量的独立数组，工作量随分量大小增长 |
| `CopyNeighbors`、`CopyIncomingNeighbors`、`CopyOutgoingNeighbors` | 写入调用方的 `List<int>`，返回结果数量 |
| `CopyConnectedNodes` | 把弱连通分量写入调用方的 `List<int>`，仍需遍历分量 |
| `TryGetTopologyVersion(factory, out long version)` | O(1) 读取工厂拓扑修订号，不扫描节点 |

所有 `Copy*` 方法的签名均为 `(PlanetFactory factory, int entityId, List<int> destination)`，返回 `int`。每次调用先清空列表；工厂或节点无效时列表为空并返回 0，`destination` 为 `null` 时抛出 `ArgumentNullException`。列表容量充足、图缓存已同步时可避免结果数组分配；缓存初始化、拓扑重建与扩容仍可能分配内存。

```csharp
private readonly List<int> neighbors = new List<int>(8);

private void RefreshNeighbors(PlanetFactory factory, int entityId)
{
    PipeTypes.CopyNeighbors(factory, entityId, neighbors);
}
```

调用方可用拓扑版本减少连通重算。库未就绪或工厂无效时返回 `false` 且版本为 0；仅在同一工厂及其运行时图生命周期内比较版本，切换存档或工厂后清除缓存。版本不表示库存、供电或生产状态。节点 ID 可以复用，因此缓存还需关联工厂和当前实体身份，并遵循下文的线程约定。

库提供通用运输、拓扑、局部截面与路径外观。区域成环、地理槽位布局、建筑附件及运行状态属于扩展维护的领域逻辑。

## 注册额外物品

```csharp
private System.IDisposable transportRegistration;

private void Awake()
{
    transportRegistration = PipeApi.RegisterTransportItems(1101);
}

private void OnDestroy()
{
    transportRegistration?.Dispose();
}
```

物品准入规则为：`(默认流体 ∪ 配置允许列表 ∪ mod 注册列表) − 配置禁止列表`。默认流体由 `ItemProto.IsFluid` 标识。禁止列表优先于任何 mod 的注册。`PipeApi.IsItemAllowed(itemId)` 查询最终准入结果；含原生储液罐的空间还有额外限制，见下文。

撤销授权或修改配置后，已保存的物品继续保存在库存中，并可取出。失去准入的物品会在新写入时被拒绝。注册和配置由运行环境管理，接收存档的玩家需要安装相同的依赖 mod 并配置物品规则。

## 查询和存取

```csharp
if (PipeApi.TryGetInventory(factory, pipeEntityId, out var inventory))
{
    long available = inventory.Count;
    double fill = inventory.FillFraction;
}

if (PipeApi.TryInsert(factory, pipeEntityId, 1000, 4, 16))
{
    // 插入后归属四个物品与十六点增产点。
}

if (PipeApi.TryTake(factory, pipeEntityId, 4, out var taken, filterItemId: 1000))
{
    int count = taken.Count;
    long points = taken.ProliferatorPoints;
}
```

`TryInsert` 全量成功或拒绝：成功后，调用方从自己的源库存扣除对应物品和增产点；拒绝的批次继续由调用方持有。数量必须为正，增产点总数范围为 `0..count×10`，参数使用 `int`。混装、满管网、容器冲突、非法参数或失效实体均返回 `false`。

`TryTake` 最多移出 `maxCount`，可能只取到部分；成功返回的物品已经从管网移除。过滤 ID 为 0 时接受当前物品。调用方应预留接收空间，并持有接收后剩余的物品。重新插入也会检查当时的准入、物品类型和容量，因此调用方负责管理接收失败后的剩余库存。返回的增产点是 `long` 类型的总点数。

`NetworkInventory`、`NetworkSnapshot`、`TransportAmount` 都是只读属性的值快照。空网 `ItemId` 可以为 0；空物流塔槽位也可保留物品过滤器。`HasConflict` 表示容器内容或过滤器存在物品类型冲突，此时均衡和存取会暂停。配置中的禁止列表在写入准入检查中生效，既有库存继续支持提取。

`TryGetInventory` 适合频繁检查库存。`TryGetNetwork` 额外给出管段数、外部容器数、管道库存和容量分项，适合面板显示。`ShareInventory` 判断两个管节点是否访问同一空间，包括通过同一储液罐或物流塔槽位形成的桥接。阀门的两个接口各自访问所在侧的空间，阀门逻辑负责侧间转移。

## 容器及原生接口

管道是无向的单一物品共享空间，同一管网中的库存立即共享。分拣器和有原生输送口的建筑按原生更新频率、堆叠、过滤和供电规则执行存取。制造建筑的生产缓冲区由原生生产逻辑管理，通过原生接口读入或输出物品。

允许固体后，管道、分拣器和物流塔槽位可以运输该物品，且继续执行单物品约束。物流塔输入口在物品明确时绑定匹配的储存槽位；未选定槽位且存在多种候选物品时，等待物品类型明确。一个槽位的容量只计入一次。

原生储液罐的储存类型为原生流体，连接储液罐的空间通过 `NetworkInventory.RequiresFluid` 标识。该约束在全局准入检查之后生效：固体写入和含固体空间的储液罐连接请求会被拒绝。既有容器出现类型冲突时，系统暂停均衡并保留库存。

管道与普通传送带间通过分拣器转移物品。连接检查会拒绝两者直接相接，也会拒绝管道接入分流器、集装机、喷涂机和流量监测器。依赖 mod 的自定义建筑通过 `PipeApi` 存取已解析的管道端点。

共享库存 API 覆盖单工厂内的管道、储液罐、物流塔和阀门。`PipeTypes` 注册独立管道原型，并复用库的建造、交互和输送接口。公共 API 不提供自定义容器注册、跨工厂连接、原子跨空间转移、程序化建造或多人同步。

## 阀门

```csharp
var settings = new ValveOptions(
    itemsPerSecond: 240,
    targetFraction: 0.9,
    direction: FlowDirection.AtoB,
    control: LevelControl.KeepB);

PipeApi.TrySetValveOptions(factory, valveEntityId, settings);
```

`ValveOptions` 是不可变设置对象，构造时验证速率、目标比例和枚举范围。速率为 `0..1,000,000` 物品/秒内的有限值，目标为 `0..1` 内的有限值。`TryGetValveOptions` 返回独立设置对象；`TrySetValveOptions` 应用设置并重置小数转移额度。方向 A/B 对应建筑原生端口 0/1。

阀门保持两侧空间独立，按库存、容量、方向、液位目标和可用电力转移。额定泵送功率约 100 kW；供电不足时按比例限速，供电为零时停止。两侧通过其它路径连通时，阀门停止转移。泵送前会执行物品准入检查，被禁止的物品继续保存在源库存中。

## 机器直连接口

`PipeApi.MachinePortsEnabled` 反映配置开关。生产建筑通过原生 `AssemblerComponent` 和分拣器插槽接入，机器的原料、产物缓存由原生逻辑管理。原生输送口建筑继续使用对应的输送口集成。

```csharp
var choices = PipeApi.GetMachinePortChoices(factory, machineEntityId);
var options = new MachinePortOptions(1000, MachinePortDirection.IntoMachine);
bool connected = PipeApi.TryConnectMachinePort(
    factory, pipeEntityId, machineEntityId, slotIndex, options);
var ports = PipeApi.GetMachinePorts(factory, pipeEntityId);
PipeApi.TrySetMachinePortOptions(
    factory, machineEntityId, slotIndex,
    new MachinePortOptions(1000, MachinePortDirection.IntoMachine, enabled: false));
PipeApi.TryDisconnectMachinePort(factory, machineEntityId, slotIndex);
```

`slotIndex` 是机器原生分拣器插槽的零起始索引，连接同时占用机器插槽与管道的一个原生连接槽。`GetMachinePortChoices` 返回配方内已获准运输的输入和产物，方向相对于机器定义。同一种物品同时作为原料和产物时，会得到两个方向各自的选项。`ItemId=0` 表示等待选择。

连接和配置请求验证配方、已有库存、插槽占用与同一共享空间的有效筛选。有效且启用的筛选会保留空网物品身份；储液罐或塔槽桥接的多个管道分量也执行同一筛选规则。配方变化或撤销准入会暂停对应接口，已有设置与库存继续保存。禁用配置后保留快照查询及断开操作，连接和配置请求返回 `false`。

输入按原生 `UpdateNeeds` 补料，通过与分拣器相同的带类型 `InsertInto` 入口执行写入；输出使用带类型 `PickFrom` 提取配方成品。每接口每 tick 最多执行 255 次原生存取。接收方拒绝后的在途物品保持归属，并随接口数据保存；拆除时返回玩家。返回的连接与选项均为独立快照。

## 线程、性能和存档

工厂操作应在 Unity 主线程，或调用方独占该工厂模拟状态的上下文执行。内部图锁保护图和库存一致性；Unity 对象和游戏原生容器的访问时序由调用方负责。调用方需要将 API 操作与同一工厂的原生更新串行安排。

稳定的管段存取通过索引访问所属连通分量的库存。共享容器操作遍历相关储存单元；拓扑改变或准入注册变化后，下一次查询可能重建容器索引。完整快照按连接的容器和管道连通分量汇总。原生容器可被其它游戏系统修改，缓存查询在模拟 tick 中协调；需要事务一致性的调用应使用上文约定的工厂独占执行上下文。

Fluid Pipes 负责自己的 DSPModSave 数据；依赖 mod 保存自身状态及其可重新解析的端点信息。库写入存档格式 6，支持读取格式 1–6。机器接口记录保存原生槽位、方向、物品筛选、启停状态和在途物品；阀门记录保存泵送设置及沙盒调节模式、接口侧、流体和目标液位。公开的 `ValveOptions` 配置普通泵送参数，沙盒调节参数通过游戏内阀门面板管理。

每个共享库存管道节点贡献 10 单位容量。加载时保留超出标称容量的库存和增产点；该空间继续支持提取，新增存入受当前容量限制。`NetworkInventory.Capacity` 返回标称容量，超容量库存的 `FillFraction` 可以大于 1。
