# 性能测量

> 本文档同时提供[英文版](PERFORMANCE.en.md)。

性能检查按核心图、原生存取、分拣器和渲染分别测量。每次比较使用同一工作负载、构建配置和运行环境，并保留代码修订、插件与游戏版本、CPU/GPU、分辨率及原始报告。

## 运行入口

先按[开发指南](../docs/DEVELOPMENT.md)配置 `Local.props`，在仓库根目录运行：

```powershell
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
```

核心检查使用 .NET 8；运行时检查在独立后台游戏进程中使用 Unity/Mono。运行时检查需要已安装的游戏和依赖，并要求退出当前游戏。默认同时加载 CopperPipes 扩展示例，`-WithoutExtensions` 只加载库。

| 负载 | 实现与结果 |
| --- | --- |
| 核心图 I/O、拆分、端点移除和锁争用 | [CoreSuite.cs](CoreSuite.cs)；`core-results.txt` |
| 原生工厂 I/O、共享容器和分拣器 | [RuntimeChecks.cs](RuntimeChecks.cs)；`runtime-results.txt`，仅库模式写入 `runtime-standalone-results.txt` |
| 轨道结构渲染 | [七环场景与记录索引](performance/seven-rings/README.md) |

## 核心图

独立检查覆盖 10、1,000、100,000 和 1,000,000 节点的链。每组预热 10,000 次存取对，再采集五组各 200,000 次存取对；一次操作指存入或取出之一。`median_ns_per_op`、`min`、`max` 是五组每次操作耗时的中位数和范围，`build_ms` 单独记录建图耗时。

拓扑负载包括切断 100,000 节点链的中间边，以及连续移除 10,000 个端点。稳态 I/O 校验拓扑访问计数不变。拆分耗时包含受影响连通分量的遍历与重新划分，不包含游戏实体、碰撞或网格更新。

争用负载通过 `Parallel.For` 调度 1、4、8 个并行工作项，访问同一个 100,000 节点库存，报告 `aggregate_Mops_s`。该负载检查图锁的吞吐与守恒；游戏对象的外部 API 调用仍遵循[线程约定](../docs/API.md#线程性能和存档)。

## 原生存取与分拣器

原生工厂负载调用经过补丁的 `PlanetFactory.InsertInto` 与 `PickFrom`。它在 10、1,000 和 100,000 节点上使用与核心图相同的预热和五组采样规模，端点在连通分量内变化。

共享容器负载连接 100,000 个管段和一个容量为 10,000 的储液罐，合计容量为 1,010,000。三组各执行 200,000 次存取对，检查数量、增产点和拓扑访问计数。`shared_large_native_io` 报告每次存入或取出的中位耗时。

分拣器负载在同一个 100,000 节点管网上推进 10,000 个原生分拣器，共 240 tick。每 tick 检查管网与分拣器持有物品的总量和增产点，报告 `average_ms_tick`、`p95_ms_tick` 和 `max_ms_tick`。计时包含分拣器状态机和库存调用；守恒断言、电力模拟、其它工厂系统和帧渲染在计时区段之外。

## 渲染与交互

渲染测量需要固定存档、相机、分辨率、图形设置和预热条件，并同时记录网格重建、提交批次、三角形、CPU 提交耗时和完整帧时间。稳态绘制与首次可见、切断重连、拆除后的重建分别采样。

[七环记录](performance/seven-rings/README.md)包含结构渲染的场景、采样口径和原始数据。`interaction-results.txt` 保存悬停、路径预览及拆除的 CPU 采样；每组结果须结合报告中的场景与构建标识解读。

`capture-scene.ps1` 和 `run-copper-interactions.ps1` 用于检查几何、原生建造和截图。截图正确性不提供完整帧率结论；离屏同步读回、后台进程和交互游戏的帧时间也使用不同口径。

## 结果边界

- .NET 8 的核心图耗时与 Unity/Mono 的游戏内耗时分别比较。
- 稳态存取不遍历拓扑；大规模断开仍可能遍历连通分量，网格重建另有成本。
- 分配计数器先用已知的 32 KiB 托管分配校准；校准失败时输出 `allocated_bytes=unavailable`，不能据此宣称零分配。物理货物数量另行检查。
- CPU 子阶段耗时、GPU 同步耗时和完整帧时间分别报告，库存基准不代表游戏帧率。
- `runtime-results.txt` 以 `Failures=0` 表示该轮检查通过；已有报告只对应生成它的构建和环境。

游戏内正确性与手动检查步骤见[测试说明](../TESTING.md)。
