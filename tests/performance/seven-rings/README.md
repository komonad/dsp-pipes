# 七环渲染测量

本目录保存固定七环场景的原始报告。每份报告开头的 `LOADED` 行标识实际加载的 Fluid Pipes 与 OrbitalRing 版本，数值对应各自的构建与采样条件。

## 场景与口径

场景使用半径 200 的原生星球，包含 4 个轨道连接组件环、2 个粒子加速轨道环和 1 个星环电网环，每环 1,000 节点。采样环境为 1600×900、Direct3D 11、NVIDIA GeForce RTX 5090，关闭音频与垂直同步，使用独立文档目录。

每个视角预热 6 帧后采集 24 帧。采样期间关闭主相机自动绘制，每帧显式绘制一次并读取一个像素等待 GPU 完成。`frame_ms` 来自 Unity 非缩放帧间隔，包含模拟、提交和同步读回。CPU 提交、同步读回和完整帧时间分别解读；该后台场景不等同于交互游戏帧率。

原生传送带参照使用星环的原生物品、模型和 CargoTraffic 渲染批次，跳过轨道的管道类型注册。预热记录还区分网格准备、首次切换视角和稳态提交。

## 报告索引

| 报告 | 采样内容 |
| --- | --- |
| [baseline.txt](baseline.txt)、[optimized.txt](optimized.txt) | 相同节点位置与相机下的近景、远景和行星视图，包含不同网格及外观配置 |
| [native-belts.txt](native-belts.txt) | 原生传送带模型参照 |
| [lod-baseline.txt](lod-baseline.txt)、[lod-optimized.txt](lod-optimized.txt) | 固定七环场景中的细节层级与提交统计 |
| [prewarm-hangers-0.10.3.txt](prewarm-hangers-0.10.3.txt) | 预热、首次视角提交、投影及整环附件检查 |
| [native-construction-0.10.4.txt](native-construction-0.10.4.txt) | 原生预建造与落成姿态、切断重连及渲染统计 |

截图位于仓库的 `screenshots/seven-rings-20261026-214101/`、`screenshots/seven-rings-lod-20261026/`、`screenshots/orbital-hangers-0.10.3/` 和 `screenshots/orbital-native-construction-0.10.4/`。

## 比较结果

比较时核对 `LOADED`、`RENDER_MODE`、场景节点和采样方式。网格、外观密度、附件和细节层级都可能改变批次及三角形数量，不能把不同配置的差异全部归因于提交算法。稳态采样需要检查网格重建计数，预热与首次可见的成本单独报告。

轨道模型与七环场景由星环扩展的场景工具验证。本库的无尽水罐、物品准入和建造栏适配使用 `tests/run-orbitalring.ps1`，运行方法见[开发指南](../../../docs/DEVELOPMENT.md)。通用库存与渲染测量边界见[性能测量](../../PERFORMANCE.md)。
