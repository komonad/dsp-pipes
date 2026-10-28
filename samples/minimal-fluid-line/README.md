# 硫酸管道产线

一座化工厂、两座行星内物流运输站、四个三级分拣器。水、精炼油和硫酸使用三个独立管网，石矿使用普通传送带。六台风力发电机和四根电力感应塔供电。

左侧物流塔的三个仓位设为沙盒无限供应：水、精炼油、石矿。右侧物流塔的硫酸仓位设为沙盒无限回收。原料和产物通过连接的管道、传送带进出。

配方每 6 秒消耗 6 精炼油、8 石矿、4 水，产出 4 硫酸。按基础配方计算，稳定产速为 40 硫酸/分钟。

## 加载

1. 使用已安装 FluidPipes 依赖的 mod profile，将随附的 `FluidPipes.dll` 放到该 profile 的 FluidPipes 插件目录。
2. 将 `minimal-fluid-line.dsv` 和 `minimal-fluid-line.moddsv` 一起复制到游戏存档目录，保持同名。
3. 加载 `minimal-fluid-line`，开启沙盒工具。玩家位于产线南侧。

存档需要 FluidPipes、CommonAPI、LDBTool、DSPModSave 及这些插件的依赖。环境配置和手动检查见[测试说明](../../TESTING.md)。

## 验证

`minimal-line-results.txt` 记录原版化工厂、分拣器、传送带、沙盒物流塔与电网的运行结果。验证覆盖三种液体隔离、满功率运行、产出与回收数量守恒，以及稳定产速。

在仓库根目录运行以下命令。构建环境配置见[开发指南](../../docs/DEVELOPMENT.md)。

重新生成场景：

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -MinimalLine
```

重载存档并运行验证：

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -MinimalLine -InspectSave samples/minimal-fluid-line/minimal-fluid-line.dsv
```
