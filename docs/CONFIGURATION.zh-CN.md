# 配置

启动游戏一次后生成 `BepInEx/config/com.komonad.dsp.fluidpipes.cfg`。以下选项在启动时读取，修改后重启游戏。

```ini
[Integration]
EnableMachinePorts = false

[Transport]
IncludeFluids = true
AllowedItemIds = 1101, 1109
DeniedItemIds = 1120

[Appearance]
MetalFittings = true

[General]
EnableDebugHotkey = false
```

| 选项 | 默认 | 含义 |
| --- | --- | --- |
| `Integration.EnableMachinePorts` | `false` | 管道直接连接生产机器的分拣器插槽；每个接口可设置方向、物品和启停 |
| `Transport.IncludeFluids` | `true` | 默认准入所有 `ItemProto.IsFluid` 物品，包含在原型初始化阶段注册的流体 |
| `Transport.AllowedItemIds` | 空 | 额外准入的物品 ID，可含固体 |
| `Transport.DeniedItemIds` | 空 | 拒绝的物品 ID，优先于默认流体、允许列表和 mod 注册 |
| `Appearance.MetalFittings` | `true` | 转角和分支接头使用不透明金属；关闭后使用透明接头 |
| `General.EnableDebugHotkey` | `false` | 启用 F8 测试快捷键，领取 200 管道和 20 阀门 |

物品列表接受逗号、分号和空白分隔的正整数；重复 ID 只计一次。准入列表以游戏数据库中存在的物品 ID 为准。若某个列表包含非法词元，该整个列表被忽略，BepInEx 日志会记录配置键和错误词元。

只允许指定物品时，将 `IncludeFluids=false` 并填写 `AllowedItemIds`。例如 `AllowedItemIds=1000,1101` 允许水和铁块；依赖 mod 仍可注册其它物品，明确禁止的 ID 始终优先。

一个连通空间始终只有一种物品。额外物品按相同的物品类型、容量、堆叠和分拣器速率规则运输。原生储液罐的储存类型为原生流体，因此连接储液罐的空间会拒绝固体写入。物流塔可使用授权物品的储存槽位共享库存。

更改准入规则后，既有库存继续保存并可取出；禁止物品的新写入和阀门转移会被拒绝。配置由当前 mod profile 管理，共享存档时需同步相关配置及依赖 mod。

## 机器直连接口

启用 `Integration.EnableMachinePorts=true` 后，管道建造工具将生产机器的分拣器插槽显示为可连接端口。适用建筑使用原生 `AssemblerComponent`，包括制造台、熔炉、化工厂和精炼厂；已有原生传送带端口的建筑继续使用自身端口机制。插槽与普通分拣器共用占用状态。

点击相连管段的“机器直连接口设置”，选择“供给机器”或“提取产物”对应的配方物品。只有一个匹配项时自动选择；流体到达或配方变化后，待配置接口会重新匹配唯一候选，其余多候选接口继续等待选择。启用且配方有效的接口筛选会约束整个共享空间，空管网也保留该约束。

输入缓存按原生需求阈值补充，输出只提取配方成品。每个接口每 tick 最多处理 255 次原生存取，持续吞吐受机器生产、需求量和管网容量约束。生产缓存由机器管理，管网库存统计覆盖管道及其共享储存容器。接口沿用物品准入规则和原生增产点语义；生产按机器自身的供电运行。

配方变化使原设置失效时，接口暂停并显示状态，原设置与管内存量继续保存。可单独暂停、重新配置或断开接口。关闭整个功能后，现有连接与配置保存在存档中，重新启用后恢复处理。
