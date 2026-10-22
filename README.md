# Fluid Pipes 0.4.0

戴森球计划的无向共享库存管道 mod，同时提供供其它 mod 依赖的 API。验证游戏版本为 0.10.35.29151。

## 安装与使用

依赖 BepInEx 5、CommonAPI、LDBTool、DSPModSave。将发布包中的 `BepInEx/plugins/FluidPipes/FluidPipes.dll` 放到对应 profile，同一 profile 只保留一份插件。

安装完成后即可研究、制造和建造管道与阀门。建筑注册、运输逻辑、渲染和交互由 Fluid Pipes 提供；其它 mod 可通过 API 扩展物品准入与存取行为。

管道和阀门随储液罐的“流体储存封装”科技解锁，制造材料、时间和批量与初级传送带相同。建造栏自动选择空位；原版环境通常位于储液罐所在栏的 F4/F5。

- 使用原生传送带风格的拖拽、格点预览、升降和碰撞检查。管道连接无方向，支持任意角度及多个分支。
- 每个管段提供 100 容量。连通管道、储液罐和绑定的物流塔槽位共享库存比例；同一空间始终只有一种物品。
- 默认运输流体，也可通过配置或依赖 mod 注册其它物品。原生储液罐保持原生流体限制。
- 点击管道可查询库存、放入或取出物品、切断单条连接、确认清空整个共享空间。
- 普通分拣器接管道后自动显示为带原生端口的管式外观，保留等级、速率、过滤和供电行为。管道与普通传送带之间通过分拣器转移物品，直接连接会被建造检查拒绝。
- 阀门有两个独立空间的接口，支持限速、单向和目标液位控制。泵送约需 100 kW 电力；两侧通过其它路径连通时，阀门停止转移。

同一管网中的库存立即共享，存取速度由端点建筑决定。生产建筑保留输入、输出角色，通过原生输送口或分拣器连接管道。图标、金属接头和透明管壁由插件提供。

## 配置

配置文件为 `BepInEx/config/com.komonad.dsp.fluidpipes.cfg`，修改后重启游戏。

```ini
[Transport]
IncludeFluids = true
AllowedItemIds = 1101, 1109
DeniedItemIds =
```

允许列表支持额外物品，禁止列表优先于默认规则和 mod 注册。修改规则保留既有库存；未获准的物品仍可取出。F8 领取测试建筑通过 `General.EnableDebugHotkey=true` 开启，默认关闭。

详见 [配置说明](docs/CONFIGURATION.zh-CN.md)。

## 作为依赖库

支持的外部接口集中在 `FluidPipes.API`，实现类型使用 `internal` 可见性。API v1 提供物品准入注册、实际原型 ID、共享库存查询、存取、空间身份判断和阀门设置。查询返回值快照，阀门配置使用不可变选项。

```csharp
[BepInDependency(PipeApi.PluginGuid, "0.4.0")]
```

编译时引用 `FluidPipes.dll` 并设置 `Private=false`。DLL 旁的 `FluidPipes.xml` 提供 IDE 文档。注册额外物品可在依赖 mod 的 `Awake` 中进行；工厂操作需等待 `PipeApi.IsReady` 并传入有效的工厂与实体 ID。

- [API 接入文档](docs/API.zh-CN.md)：生命周期、库存语义、线程、单位、失败行为和兼容性。
- [可编译示例](examples/PipeConsumer/ConsumerPlugin.cs)：依赖声明、授权生命周期和有界存取。
- [架构说明](docs/ARCHITECTURE.md)：模块边界、库存守恒、原生接口和性能约束。
- [渲染说明](docs/RENDERING.md)：程序化建模、材质和截图场景。

API v1 的集成范围是单工厂内现有管道、储液罐、物流塔和阀门的共享库存。自定义容器、多人物流同步和跨工厂管网需要进一步扩展接口。分馏塔使用原生交互路径。

## 开发与验证

```powershell
dotnet build FluidPipes.csproj -c Release
dotnet build examples/PipeConsumer/PipeConsumer.csproj -c Release
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
pwsh -NoProfile -File package.ps1
```

参见 [开发指南](docs/DEVELOPMENT.md)、[手动测试说明](TESTING.zh-CN.md) 和 [性能测量](tests/PERFORMANCE.md)。自动检查覆盖 API 文档、准入优先级、原生货运适配、库存守恒、储存容器、阀门及大图 I/O。运行时测试使用独立的后台游戏进程；交互帧率和第三方运输补丁兼容性需在实际存档中验证。

发布包包含 mod DLL。示例存档、截图和测试结果位于仓库中，基础依赖通过 mod 管理器安装。
