# FCP（暂定名）

一款面向 **PCL2 用户**的 Minecraft Java 版 Windows 启动器：

- **熟悉**：启动页、版本选择、版本设置、设置项的位置和逻辑延续 PCL 的习惯，老用户不需要学习。
- **简单**：自动找到你已有的游戏、版本、Mod 和存档；自动选择 Java、分配内存；出问题时用大白话告诉你怎么办。
- **稳定**：原地使用已有的游戏文件，不修改 PCL 的任何数据，两个启动器可以随时交替使用。

> 当前阶段：开发中（尚未发布）。已可识别 PCL 的游戏文件夹、版本、Mod、存档和设置，并用离线账号启动原版 / Forge / Fabric。

## 构建
需要 Windows 和 .NET 10 SDK。

```
dotnet build FCP.slnx
dotnet test --solution FCP.slnx
dotnet publish src/FCP.App -c Release -o publish
```

## 致谢
本项目是独立实现的启动器，与 Plain Craft Launcher（PCL）没有隶属关系。迁移功能兼容 PCL 的数据格式。感谢 PCL 及其社区为国内 Minecraft 启动器体验所做的开创性贡献。

## 许可证 / License

本项目以 [MIT 许可证](LICENSE) 开源。第三方组件的许可见 [NOTICE.md](NOTICE.md)。本项目与 PCL 及其作者没有隶属关系；Minecraft 是 Mojang Studios 的商标，本启动器不提供游戏本体下载。

Released under the [MIT License](LICENSE). See [NOTICE.md](NOTICE.md) for third-party licenses. Not affiliated with PCL or Mojang Studios; this launcher does not distribute the Minecraft game.
