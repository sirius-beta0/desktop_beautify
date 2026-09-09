# 第三方开源组件声明 / Third-Party Notices

本项目 **DesktopBeautify Launcher** 自身以 [Apache-2.0](./LICENSE) 协议开源。
在构建与发布过程中，以下开源组件（以 NuGet 包形式）被直接或间接引用。
本文件依据各组件许可证（均为 MIT）的要求，对其作者与版权进行声明与致谢。

> 说明：下列全部组件均以 **MIT License** 分发。MIT 许可证全文见文件末尾。
> 版本号取自 `dotnet list package --include-transitive`（`src/Launcher.App`，net8.0-windows）。

---

## 直接依赖（Direct Dependencies）

| 组件 | 版本 | 许可证 | 版权 / 作者 | 来源 |
|---|---|---|---|---|
| Vanara.Windows.Shell | 5.0.7 | MIT | Copyright © 2017–2026 David Hall | https://github.com/dahall/Vanara |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Copyright © .NET Foundation and Contributors | https://github.com/CommunityToolkit/dotnet |

## 间接依赖 / 传递依赖（Transitive Dependencies）

以下组件由上述直接依赖引入（同属 Vanara 系列，均为 MIT，Copyright © 2017–2026 David Hall，来源 https://github.com/dahall/Vanara）：

- Vanara.Core 5.0.7
- Vanara.PInvoke.ComCtl32 5.0.7
- Vanara.PInvoke.Cryptography 5.0.7
- Vanara.PInvoke.Gdi32 5.0.7
- Vanara.PInvoke.Kernel32 5.0.7
- Vanara.PInvoke.Ole 5.0.7
- Vanara.PInvoke.Rpc 5.0.7
- Vanara.PInvoke.SearchApi 5.0.7
- Vanara.PInvoke.Security 5.0.7
- Vanara.PInvoke.Shell32 5.0.7
- Vanara.PInvoke.ShlwApi 5.0.7
- Vanara.PInvoke.User32 5.0.7
- Vanara.Windows.Extensions 5.0.7
- Vanara.Windows.Shell.Common 5.0.7

以下组件由 .NET 运行库 / 框架引入（均为 MIT，Copyright © Microsoft Corporation，来源 https://github.com/dotnet/runtime）：

- Microsoft.Win32.SystemEvents 10.0.2
- System.Configuration.ConfigurationManager 10.0.2
- System.Diagnostics.EventLog 10.0.2
- System.Drawing.Common 10.0.2
- System.Security.Cryptography.ProtectedData 10.0.2
- System.Security.Permissions 10.0.2
- System.Windows.Extensions 10.0.2

## 运行时（Runtime）

- **.NET 8（Microsoft.NETCore.App）** — MIT，Copyright © .NET Foundation and Contributors，
  来源 https://dotnet.microsoft.com/ 。本项目自包含发布（self-contained）会将 .NET 8 运行库一并打包分发。

---

## MIT License

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

*本声明由维护者整理，如与任一上游组件的官方许可证文本存在出入，以上游组件官方仓库中的许可证文本为准。*
