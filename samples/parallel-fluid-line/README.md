# 平行管道与传送带产线

三座塑料化工厂，两座沙盒行星内物流运输站，九个生产分拣器。南侧高能石墨带靠内、精炼油管靠外；北侧精炼油管靠内、塑料带靠外。南侧管式分拣器跨越传送带，北侧普通分拣器跨越管道。

左右两端连接两条精炼油管道，形成一个共享库存的无向环网。延长平行干线即可继续增加化工厂。

左塔无限供应精炼油和高能石墨，右塔无限回收塑料。按基础配方计算，稳定总产量为 60 塑料/分钟。十二台风力发电机和十根电力感应塔供电。

产线南侧的短管、短传送带和管式分拣器组成独立的空载展示装置，拥有单独的管网。

## 加载

1. 使用已安装 FluidPipes 依赖的 mod profile，将随附的 `FluidPipes.dll` 放到该 profile 的 FluidPipes 插件目录。
2. 将 `parallel-fluid-line.dsv` 和 `parallel-fluid-line.moddsv` 一起复制到游戏存档目录，保持同名。
3. 加载 `parallel-fluid-line`，开启沙盒工具。玩家位于产线南侧。

存档需要 FluidPipes、CommonAPI、LDBTool、DSPModSave 及这些插件的依赖。环境配置和手动检查见[测试说明](../../TESTING.md)。

## 验证

`parallel-line-results.txt` 记录存档重载、稳定产量、库存守恒、建造栏按钮及管道/传送带之间的分拣器存取检查。

在仓库根目录重新生成及重载验证，构建环境配置见[开发指南](../../docs/DEVELOPMENT.md)：

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine -InspectSave samples/parallel-fluid-line/parallel-fluid-line.dsv
```
