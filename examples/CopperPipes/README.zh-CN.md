# Copper Pipes

Copper Pipes 是依赖 FluidPipes 0.8.0 的独立扩展 Mod。它添加铜管物品、制造配方和铜色金属外观，管壁与接头使用相同材质参数。FluidPipes 的默认玻璃管作为独立物品提供；铜管的原型注册独立于内置玻璃管和阀门。

铜管沿用共享库存与无向连接规则，每段容量 10。铜管与玻璃管可以相互连接，并共享同一物品库存和液位。物品准入策略由 FluidPipes 的配置决定。

## 游戏中使用

研究解锁储液罐的科技后，在制造面板的建筑页找到“铜管”。铜管配方沿用初级传送带的材料、数量、制作时间和产量；可手工制造或由制造台生产。从背包拿起铜管即可拉线建造。

制造格位在原型数据库合并后从建筑页的原生七行、十四列中选择，检查所有已注册物品与配方占用的格位。默认玻璃管与铜管使用不同格位。加载已解锁储液罐配方的存档时，扩展会补充解锁铜管配方。

铜管物品与制造配方使用独立的铜色管道图标。图标源文件为 `assets-src/copper-pipe.png`，作为资源嵌入扩展 DLL。此扩展通过背包手持进入建造模式。

## 安装

安装 FluidPipes 0.8.0 及其依赖后，将 `CopperPipes-1.0.1.zip` 解压到对应游戏或 Mod profile 根目录。扩展文件位于 `BepInEx/plugins/CopperPipes/CopperPipes.dll`。

使用包含铜管建筑或物品的存档时，保持 Copper Pipes 与 FluidPipes 一同安装。LDBTool 管理物品和配方 ID，跨 profile 迁移存档时一并保留原有 LDBTool ID 配置。

## 编译与打包

项目读取仓库根目录的 `Local.props`，使用其中的游戏程序集和 BepInEx 路径。

```powershell
dotnet build examples/CopperPipes/CopperPipes.csproj -c Release
powershell -NoProfile -File examples/CopperPipes/package.ps1
```

也可直接引用已编译的 FluidPipes.dll。指定 `FluidPipesAssemblyPath` 时，项目采用 DLL 引用编译：

```powershell
dotnet build examples/CopperPipes/CopperPipes.csproj -c Release -p:FluidPipesAssemblyPath='D:\Mods\FluidPipes.dll'
```

独立使用此示例目录时，可通过 MSBuild 参数提供 `Managed`（游戏 `DSPGAME_Data/Managed`）、`BepInExCore`（profile 的 `BepInEx/core`）与 `FluidPipesAssemblyPath`。`ModPlugins` 默认位于 `BepInExCore/../plugins`，用于查找 CommonAPI 与 LDBTool。

包输出到 `examples/CopperPipes/dist/CopperPipes-1.0.1.zip`，只包含内嵌图标的 CopperPipes.dll。FluidPipes 作为独立依赖安装。

## 扩展示例结构

插件 GUID 为 `example.copperpipes`，类型键为 `example.copperpipes:copper`，默认物品与配方 ID 均为 `11960`。运行时日志记录 LDBTool 最终采用的 ID、格位与科技。

`PipeTypes.CreateItem` 创建独立的物品原型，`PipeTypes.SetItemIcon` 为物品设置扩展持有的 Sprite，`PipeTypes.Register` 注册连接类型。配方通过结果物品取得相同图标。`PipeAppearance` 配置库拥有的管壁和接头材质副本。`PipeBehavior.SharedInventory` 配合默认连接家族，使铜管与普通玻璃管共享库存。原型、配方和科技关联由游戏与 LDBTool 的公开接口完成。

源码仅引用 `FluidPipes.API`，以独立程序集编译。扩展的材质配置作用于铜管类型拥有的副本。

运行时图标转换为原生图集使用的 80×80 ARGB32 纹理与 FullRect Sprite。纹理保留 CPU 像素供图集读取，Sprite 与纹理由扩展持有至应用退出，以覆盖数据库引用和图集重建。
