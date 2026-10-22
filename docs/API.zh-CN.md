# Fluid Pipes API v1

Fluid Pipes 同时提供管道建筑和供其它 BepInEx mod 使用的共享库存 API。安装插件及基础依赖后，即可使用管道和阀门的建造、运输、渲染与交互功能。外部集成通过 `FluidPipes.API` 命名空间访问同一套运行逻辑，实现类型使用 `internal` 可见性。

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
[BepInDependency(PipeApi.PluginGuid, "0.4.0")]
public sealed class ConsumerPlugin : BaseUnityPlugin
{
}
```

Fluid Pipes 的 GUID 是 `com.komonad.dsp.fluidpipes`。运行时需要安装 Fluid Pipes 及其依赖 BepInEx 5、CommonAPI、LDBTool、DSPModSave。仓库中的 [可编译示例](../examples/PipeConsumer/PipeConsumer.csproj) 展示依赖声明、注册生命周期和存取方式。

`PipeApi.ApiVersion` 表示 API 契约代数；`PipeApi.ModVersion` 表示插件发行版本。API v1 的兼容升级保留现有签名和语义；不兼容变更需要新的契约代数和发行说明。插件程序集版本保持 `1.0.0.0`，文件版本跟随发行版本。

## 初始化和生命周期

在依赖 mod 的 `Awake` 中可调用 `RegisterTransportItems`，物品 ID 会在游戏数据库初始化时解析。注册参数数组被复制；相同物品可由多个 mod 独立注册。注册返回 `IDisposable`，在所属插件的 `OnDestroy` 中释放。每个注册和配置授权分别管理生命周期，物品在至少一项授权有效时保持准入。

注册、释放必须在 Unity 主线程执行。数据库建立前的物品注册在初始化时解析，因此新增物品应通过 LDBTool 的原型注册阶段加入数据库。初始化后，注册以数据库中已经存在的物品 ID 为准。

游戏原型加载完成后 `PipeApi.IsReady` 为 `true`；此前各建筑、配方 ID 属性返回 0，库存操作返回 `false`。依赖 mod 通过 `PipeApi.PipeItemId`、`ValveItemId`、`PipeRecipeId`、`ValveRecipeId` 获取实际 ID。

API 通过工厂的 `entityPool` 定位目标，调用方传入正数的 **实体 ID (`EntityData.id`)**。实体拆除后 ID 可能复用；切换存档、拆除和重新建造后，调用方需要重新解析工厂与实体绑定。

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
    // Insertion owns four items and sixteen total proliferator points.
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

API v1 的集成范围是单工厂内现有管道、储液罐、物流塔和阀门的共享库存。自定义容器注册、替换管道原型、跨工厂连接、原子跨空间转移、建造接口和多人同步协议需要进一步扩展 API。

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

## 线程、性能和存档

工厂操作应在 Unity 主线程，或调用方独占该工厂模拟状态的上下文执行。内部图锁保护图和库存一致性；Unity 对象和游戏原生容器的访问时序由调用方负责。调用方需要将 API 操作与同一工厂的原生更新串行安排。

稳定的管段存取通过索引访问所属连通分量的库存。共享容器操作遍历相关储存单元；拓扑改变或准入注册变化后，下一次查询可能重建容器索引。完整快照按连接的容器和管道连通分量汇总。原生容器可被其它游戏系统修改，缓存查询在模拟 tick 中协调；需要事务一致性的调用应使用上文约定的工厂独占执行上下文。

Fluid Pipes 负责自己的 DSPModSave 数据；依赖 mod 保存自身状态及其可重新解析的端点信息。当前写入存档格式 4，读取格式 1–4。物品准入扩展沿用该格式，保存整数数量、物品 ID 和增产点。
