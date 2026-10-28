# 开发指南

> 本文档同时提供[英文版](DEVELOPMENT.en.md)。

## 本地构建

以下命令在仓库根目录执行。依赖 profile 需安装 BepInEx、CommonAPI、LDBTool、DSPModSave 及其依赖；运行时测试脚本还会复制 profile 中的 NebulaMultiplayerModApi。

将 `Local.props.example` 复制为 `Local.props`。把 `Managed` 设为已安装游戏的管理程序集目录，把 `BepInExCore` 设为依赖 profile 的 BepInEx 核心目录。原生运行时夹具还会使用由 `GameAssembly` 选择的公有化（publicized）程序集；正式插件引用未修改的游戏程序集。

插件目标框架为 .NET Framework 4.7.2 和 C# 7.3。需要 .NET SDK、框架引用程序集和 PowerShell 7。独立核心检查的目标框架为 .NET 8。

```powershell
dotnet build FluidPipes.csproj -c Release
dotnet build examples/PipeConsumer/PipeConsumer.csproj -c Release
dotnet build examples/CopperPipes/CopperPipes.csproj -c Release
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
dotnet build tests/SceneCapture.csproj -c Release
```

`bin/Release/FluidPipes.dll` 是运行时插件。与其同目录的 `FluidPipes.xml` 为 API 使用方提供 IDE 文档。库存使用方示例编译为 `PipeConsumer.dll`。材质扩展编译为 `CopperPipes.dll`；它通过公开 API 注册独立物品和配方。设置 CopperPipes 项目的 `FluidPipesAssemblyPath` 属性可指定要引用的 DLL。

## 验证

核心测试套件包含一百万节点的库存基准测试、随机化连通性/守恒检查和并发图操作。运行时检查会启动隔离的隐藏游戏进程，验证原生 Harmony 集成，并在写入 `tests/runtime-results.txt` 后退出。这些检查还要求每个导出的 API 成员都带有 XML 摘要和参数文档，并拒绝在 API 命名空间之外暴露实现类型。

默认运行时运行会构建 `CopperPipes.dll` 并将其作为独立的 BepInEx 插件加载。其测试检查独立原型与材质、铜/玻璃混合管网、原生插入与提取、持久化和拆除。`pwsh -NoProfile -File tests/run.ps1 -WithoutExtensions` 在该插件缺失时运行，并写入 `tests/runtime-standalone-results.txt`。`screenshots/pipe-copper-consumer.png` 中的材质对比使用 Unity 运行时夹具中的实时管道渲染器。

`pwsh -NoProfile -File tests/run-copper-interactions.ps1` 在铺满地基的独立沙盒中演练原生路径与分拣器建造工具。相互对应的玻璃和铜制路径覆盖不同朝向、高度变化和拖拽方向。该夹具验证预览与建成姿态、待建网格标记、拆除悬停标识以及管道侧分拣器附着框。结果和端点截图复制到 `screenshots/copper-interactions/`。

场景测试装置会创建隔离沙盒，用原生地基覆盖整个星球，并将游戏相机渲染到离屏纹理。生产夹具可导出原生存档和配套 mod 数据。验证环境只支持一个活动游戏实例，因此启动测试装置进程前需退出当前游戏。

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine -InspectSave obj/scene-capture/profile/BepInEx/plugins/scene-output/parallel-fluid-line.dsv
```

并行夹具包含三座塑料化工厂、沙盒源/汇物流塔以及并行的管道/传送带通道。它检查稳定生产、数量守恒、管道到传送带的分拣器转移和原生建造栏选择。存档和配套 mod 数据保存在 `obj/scene-capture/profile/BepInEx/plugins/scene-output/`，截图与报告同时复制到 `screenshots/ingame/`。其它测试装置模式覆盖几何、连接模式、立方体管网、阀门交互和分拣器外观。

性能报告标明运行时、工作负载和所测量的 CPU 阶段。交互式帧率评估需要完整加载的工厂和帧级测量。运行时检查先通过已知分配验证分配计数器；验证失败时标记为不可用，物理货物数量另行校验。参见[性能测量](../tests/PERFORMANCE.md)。

OrbitalRing 兼容性测试装置会把已安装 OrbitalRing profile 中的核心文件、预加载器和所需插件复制到 `obj/orbitalring-checks/` 下的全新目录。它启动带隔离文档/存档目录的隐藏沙盒。需显式选择源 profile：

```powershell
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile -WithBuildBarTool
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile -Vanilla
```

供给用例通过原生库存窗口激活无限储液罐，验证持续输出、背压、独立计速、输入激活、混液限制与库存守恒。每个用例同步步进 `FactoryStorage.GameTickTank` 600 次，以 60 tick/s 计算应输出 300 物品。输出端口探针每 tick 清空蓝色传送带缓冲区；该探针测量原生插入尝试，传送带吞吐测量还需推进传送带模拟。`orbitalring-results.txt` 记录加载版本与测量结果；`Completed=1` 表示所有断言通过。

建造用例使用原生路径判定、建造条件、待建放置和完成逻辑。水平路径和高架路径以相反顺序完成；每次完成都会检查相邻且互为对应的原生插槽，完成后的路径支持远程库存提取。原版（vanilla）模式不加载 OrbitalRing，运行相同的建造用例。BuildBarTool 模式还会校验运输分类上排的两个物品绑定。

机器直连夹具在独立隐藏沙盒中启用 `Integration.EnableMachinePorts`，验证硫酸生产的多液体隔离、原生 I/O、需求限制、背压、预留、配方变化、建造与持久化。同一夹具检查库存窗口的共享存取和清空、节点描边，以及沙盒阀门的液位模式、侧选择与增产点守恒。截图和报告写入 `obj/machine-port-checks/` 并复制到 `screenshots/machine-ports/`；`-InspectSave` 验证指定存档的重载。

```powershell
pwsh -NoProfile -File tests/run-machine-ports.ps1
pwsh -NoProfile -File tests/run-machine-ports.ps1 -InspectSave C:/path/to/machine-ports.dsv
```

## API 维护

除 BepInEx 插件入口点外，公开类型都归属于 `FluidPipes.API`。公开类型、方法、属性和枚举值使用 PascalCase；参数和私有字段使用 camelCase。公开 API 成员需带有 XML 摘要、参数含义、单位、失败行为以及适用的生命周期/线程约束。内部注释记录不变式和游戏 API 约束。

格式使用固定版本的本地 CSharpier 工具和 `.csharpierrc.json`。通过 `dotnet tool restore` 还原，运行 `dotnet csharpier format .` 执行格式化，运行 `dotnet csharpier check .` 校验。使用显式大括号，每条语句只声明一个变量。玩法变量按其在领域中的角色命名；几何相关变量可使用常规坐标记法。Harmony 钩子的参数名遵循原生签名，因为 Harmony 按名称绑定这些参数。

对外门面独立于 Harmony 补丁类。物品插入路径在共享库存边界执行准入。对于普通传送带目标，货运钩子将控制权交回原生方法。存储和拓扑变更需要验证序列化、重载与库存守恒。

## 文档与注释语言

文档以简体中文为主：主文件使用 `*.md`，英文译本使用同名的 `*.en.md`，并在文首互相链接。代码注释和 XML 文档注释使用中文。新增或修改的公开 API 文档先写中文；术语保持一致，代码标识符、路径和专有名词保留原文。

## 打包

```powershell
pwsh -NoProfile -File package.ps1
```

归档包仅包含 `BepInEx/plugins/FluidPipes/FluidPipes.dll`。依赖、示例插件、存档、截图和验证输出保持独立。`play.ps1 -PrepareOnly` 准备本地 profile；`-ProfilePath` 选择其目标位置。

`examples/CopperPipes/package.ps1` 单独打包材质扩展。其归档包仅包含 `BepInEx/plugins/CopperPipes/CopperPipes.dll`；FluidPipes 提供运行时依赖。
