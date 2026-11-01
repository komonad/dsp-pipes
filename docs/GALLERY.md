# 游戏内图库

场景使用内置玻璃管道和真实原生分拣器，通过游戏相机及插件渲染器生成 2560×1440 截图。流体颜色沿用原生储液罐调色板。

## 玻璃管正多面体

五件几何体各自构成独立的共享库存管网。

| 正多面体 | 边长 | 流体 |
| --- | ---: | --- |
| 四面体 | 8 | 精炼油 |
| 立方体 | 7 | 水 |
| 八面体 | 7 | 原油 |
| 十二面体 | 6 | 硫酸 |
| 二十面体 | 7 | 重氢 |

边长以 Unity 世界单位计。顶点与沿边采样点位于每件几何体的局部正交坐标系中。

![五种流体的玻璃管正多面体](images/fluid-solids.png)

## 立体网格

网格包含 4×4×4 个立方单元，每格边长为 3，整体边长为 12。125 个交点与 300 条格边由 725 个管道节点和 900 段连接构成；六向交汇与全部边共享同一份水库存。

![单格边长为3的玻璃管立体网格](images/fluid-lattice.png)

## 流体浑天仪与双螺旋塔

浑天仪包含五个不同倾角与半径的流体圆环、中心两个互锁环和一个水平底环。807 个玻璃管道节点组成八条独立管路，外圈直径为 36 世界单位。

![玻璃管流体浑天仪](images/fluid-armillary.png)

![黄昏的流体浑天仪](images/fluid-armillary-dusk.png)

双螺旋塔的两条管路分别装有水与重氢，半径为 6.4，上升 42 世界单位并旋转三圈。三个硫酸水平环分别位于底部、中部与顶部，730 个节点组成五条独立管路。

![玻璃管双螺旋塔](images/fluid-double-helix.png)

![黄昏的双螺旋塔](images/fluid-double-helix-dusk.png)

[原生蓝图与配色说明](../blueprints/README.md)包含两座奇观的可导入文本。蓝图保存位置和连接，建成后需要单独注入流体。两份蓝图经过游戏原生预览、碰撞检查、待建生成、实际建造和逐边拓扑验证。

## 液体分拣器

阵列从左到右为分拣器、高速分拣器、极速分拣器和集装分拣器；每种等级展示 1、2、3 三种跨度。玻璃臂、金属弯管、端点管箍与等级箭头随实际端点位置生成。

![四等级三跨度分拣器](images/liquid-inserters.png)

连接普通传送带的分拣器保留原生等级机壳；管道侧使用贴合管壁的鞍座。下图展示四种等级的传送带端与管道端。

![四种等级的传送带与管道接口](images/liquid-inserter-housings.png)

![极速与集装分拣器机壳近景](images/liquid-inserter-detail.png)

高差连接保持同轴的端点管箍与连续的玻璃臂。

![液体分拣器高差连接](images/liquid-inserter-elevated.png)

## 生成场景

按[开发指南](DEVELOPMENT.md)配置本机环境，退出当前游戏，在仓库根目录运行：

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -SolidsGallery
pwsh -NoProfile -File tests/capture-scene.ps1 -InserterGallery
pwsh -NoProfile -File tests/capture-scene.ps1 -WondersGallery
```

命令分别启动隔离的后台沙盒。完整 PNG 截图和检查报告写入 `screenshots/showcase/`，奇观蓝图同时写入其 `blueprints/` 子目录。几何体检查连接度数、单一流体身份、容量和远端提取守恒；分拣器检查原生组件、端点角色、显示归属、库存隔离与供电标记。奇观场景调整隔离星球的自转相位，使用原生光照拍摄白天和黄昏视图。
