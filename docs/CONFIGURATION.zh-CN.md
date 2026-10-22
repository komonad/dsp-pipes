# 配置

启动游戏一次后生成 `BepInEx/config/com.komonad.dsp.fluidpipes.cfg`。以下选项在启动时读取，修改后重启游戏。

```ini
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
| `Transport.IncludeFluids` | `true` | 默认准入所有 `ItemProto.IsFluid` 物品，包含在原型初始化阶段注册的流体 |
| `Transport.AllowedItemIds` | 空 | 额外准入的物品 ID，可含固体 |
| `Transport.DeniedItemIds` | 空 | 拒绝的物品 ID，优先于默认流体、允许列表和 mod 注册 |
| `Appearance.MetalFittings` | `true` | 转角和分支接头使用不透明金属；关闭后使用透明接头 |
| `General.EnableDebugHotkey` | `false` | 启用 F8 测试快捷键，领取 200 管道和 20 阀门 |

物品列表接受逗号、分号和空白分隔的正整数；重复 ID 只计一次。准入列表以游戏数据库中存在的物品 ID 为准。若某个列表包含非法词元，该整个列表被忽略，BepInEx 日志会记录配置键和错误词元。

只允许指定物品时，将 `IncludeFluids=false` 并填写 `AllowedItemIds`。例如 `AllowedItemIds=1000,1101` 允许水和铁块；依赖 mod 仍可注册其它物品，明确禁止的 ID 始终优先。

一个连通空间始终只有一种物品。额外物品按相同的物品类型、容量、堆叠和分拣器速率规则运输。原生储液罐的储存类型为原生流体，因此连接储液罐的空间会拒绝固体写入。物流塔可使用授权物品的储存槽位共享库存。

更改准入规则后，既有库存继续保存并可取出；禁止物品的新写入和阀门转移会被拒绝。配置由当前 mod profile 管理，共享存档时需同步相关配置及依赖 mod。
