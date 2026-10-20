# 第三方声明

本项目是独立实现的 Minecraft 启动器，与 Plain Craft Launcher（PCL）及其作者没有隶属关系。
迁移功能为了数据互通而兼容 PCL 的配置格式。感谢 PCL 及其社区为国内 Minecraft 启动器体验所做的开创性贡献。

## 引用的代码与资源

| 组件 | 许可 | 用途 | 来源 |
|---|---|---|---|
| MeloongCore — `StringExtensions.GetStableHashCode` 算法 | Apache-2.0，Copyright Meloong-Git | 计算与 PCL 兼容的离线 UUID（`src/FCP.Core/Accounts/OfflineUuid.cs`） | https://github.com/Meloong-Git/MeloongCore |
| Fluent UI System Icons | MIT，Copyright (c) 2020 Microsoft Corporation | 界面图标（`src/FCP.UI/Themes/Icons.xaml`） | https://github.com/microsoft/fluentui-system-icons |

## 依赖的程序包

| 程序包 | 许可 |
|---|---|
| CmlLib.Core | MIT |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.* | MIT |
| Serilog 及其扩展 | Apache-2.0 |
| System.CommandLine | MIT |
| xunit.v3、Shouldly（仅测试） | Apache-2.0 / BSD-3-Clause |

## 运行时下载的程序

| 程序 | 许可 | 说明 |
|---|---|---|
| authlib-injector | AGPL-3.0（附 authlib-injector 例外条款） | 使用第三方登录时在运行时下载，作为独立程序以 Java agent 方式加载，不与本项目链接 |
