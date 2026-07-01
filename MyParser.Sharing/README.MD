# MyParser.Sharing

`MyParser.Sharing` 是 Shirobot.Plugin.MyParser 的 provider 共享抽象层，构建产物为：

```text
myparser-sharing.dll
```

它被主插件和各个平台 provider 同时引用，用于定义 provider contract、通用 DTO、配置模型和轻量工具。它不承载具体平台业务，也不直接实现下载、上传、渲染或消息发送等宿主能力。

## 职责边界

`MyParser.Sharing` 适合放：

- provider contract / interface。
- 解析结果 DTO。
- provider module / message handler 的基础抽象。
- 轻量工具方法。
- 插件配置模型。
- 运行时轻量状态。

不应该放：

- 具体平台解析逻辑。
- 视频 / 音频下载器实现。
- 本地 HTTP 服务实现。
- 文件上传实现。
- Avalonia 渲染实现。
- ShiroBot 消息发送具体编排。
- Cookie 文件监听、插件目录扫描等宿主侧实现。

这些能力应放在主插件 `MyParser/` 中，通过 `IProviderHostServices` 暴露给 provider；或者放在对应 `MyParser.Provider.*` 中作为平台差异实现。

## 目录结构

```text
MyParser.Sharing/
  MyParser.Sharing.csproj
  PluginConfig.cs
  Parsing/
    IParseProvider.cs
    IIncomingMessageParseProvider.cs
    MediaParseResult.cs
    ParseProviderRegistry.cs
    ProviderBase.cs
    ProviderContracts.cs
  Utility/
    MyParserRuntime.cs
```

## 核心类型

### 解析 provider

- `IParseProvider`
  - provider 基础解析接口。
  - 定义 `Id`、`Name`、`CanHandle(text)` 和 `ParseAsync(text)`。
- `IIncomingMessageParseProvider`
  - 允许 provider 从完整 `IncomingMessage` 中提取待解析文本。
  - 常用于 QQ 轻应用 / 小程序 payload 链接提取。
- `IParseProviderWithParser`
  - 暴露内部 parser 对象，供平台命令或 handler 复用。
- `IProviderPriority`
  - provider 匹配优先级，用于同平台多 provider 或短链场景排序。

### 解析结果

- `MediaParseResult`
  - 各 provider 返回给主调度层的统一解析结果。
  - 包含媒体 ID、标题、作者、封面、音乐 URL、标签、是否视频 / 图集，以及 `ProviderPayload`。
  - 平台专用详细模型应放在 `ProviderPayload` 中。

### Provider module

- `IMyParserProviderModule`
  - provider dll 对外暴露的模块入口。
  - 主插件扫描 `myparser-provider-*.dll` 后通过该接口发现 provider。
- `MyParserProviderModuleBase`
  - provider module 推荐基类。
- `IProviderMessageHandlerFactory`
  - provider 需要自定义消息发送流程时实现。
- `IProviderRuntimeModule`
  - 用于承载 provider 运行时加载、命令、帮助和状态输出等能力。

### 消息处理

- `IProviderMessageHandler`
  - provider 自定义消息处理入口。
  - 定义 `ParseAndReplyAsync` 和 `HandleLoginAsync`。
- `ProviderMessageHandlerBase`
  - message handler 推荐基类。
  - 封装 `ReplyAsync`、`ReactAsync`、`SendImageAsync`、`ResolveCookiePath` 等常用宿主能力调用。
- `ProviderMessageHandlerContext`
  - message handler 的依赖上下文，包括 `IBotContext`、`PluginConfig`、`ParseProviderRegistry`、当前 provider 和 `IProviderHostServices`。

### 宿主能力

- `IProviderHostServices`
  - 主插件向 provider 暴露的宿主能力集合。
  - 包括回复文本、发送图片、reaction、Cookie 路径、上传本地文件、注册本地视频 HTTP、远程图片下载、图片解码、范围下载和并发选择等。

provider 应优先通过 `IProviderHostServices` 使用宿主能力，不要反向引用主插件项目或复制主插件实现。

### 登录、Cookie 与命令

- `IProviderLoginStatusProvider`
  - 登录态检查接口。
- `IQrLoginProvider`
  - 扫码登录接口。
- `ICookieValidator`
  - Cookie 格式校验接口。
- `IProviderCookieStore`
  - provider Cookie 描述符集合。
- `ProviderCookieDescriptor`
  - Cookie 文件名、应用方法、校验方法和提示文案。
- `IProviderCommandContributor`
  - provider 贡献平台专用命令。
- `ProviderCommandDescriptor`
  - 命令名、处理函数和是否 AdminOnly。

### 文本、自动解析与回复选择

- `IProviderTextNormalizer`
  - 普通文本归一化。
- `IIncomingProviderTextNormalizer`
  - 从完整消息做文本归一化。
- `IProviderAutoParsePolicy`
  - provider 自动解析开关判断。
- `IProviderResultMessageClassifier`
  - 判断消息是否为插件自身输出，避免重复解析。
- `IProviderReplyParseTextBuilder`
  - 支持“回复上一条候选列表 + 序号”一类延迟解析流程。

## 配置模型

`PluginConfig.cs` 定义插件运行配置，包含：

- 通用下载、发送、清理配置。
- 各平台自动解析开关。
- Bilibili 直播回溯配置。
- 小红书 sign 服务配置。
- 网易云音乐语音发送配置。

配置字段通过 `ShiroBot.SDK.Config.ConfigField` 标注，供 ShiroBot 配置 UI / 配置上下文使用。

## 运行时状态

`Utility/MyParserRuntime.cs` 保存轻量运行时状态，例如平台 Cookie 字符串。它只保存运行时值，不负责文件监听、加载和持久化策略。

Cookie 文件加载、热重载和保存逻辑由主插件或 provider handler 调度。

## Provider 接入最小示例

```csharp
public sealed class ExampleProviderModule : MyParserProviderModuleBase
{
    public override string Id => "example";
    public override string DisplayName => "Example";

    public override IReadOnlyList<IParseProvider> CreateProviders(PluginConfig config)
    {
        return [new ExampleParseProvider(config)];
    }
}

public sealed class ExampleParseProvider(PluginConfig config) : IParseProvider
{
    public string Id => "example";
    public string Name => "Example";

    public bool CanHandle(string text) => text.Contains("example.com", StringComparison.OrdinalIgnoreCase);

    public Task<MediaParseResult> ParseAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new MediaParseResult
        {
            ProviderId = Id,
            ProviderName = Name,
            MediaId = "example-id",
            SourceUrl = text,
            Title = "Example title",
            IsVideo = false,
            IsGallery = false,
            Tags = [],
            ProviderPayload = null,
        });
    }
}
```

## 开发约定

- 保持 `MyParser.Sharing` 轻量、稳定，避免把平台细节或宿主实现塞进共享层。
- 新增 contract 前先确认是否至少有两个使用方，或是否确实属于主插件与 provider 的边界能力。
- 变更 public interface 时，需要同步检查所有 provider 是否实现受影响接口。
- 不要在共享层引入重量级依赖；当前只保留必要的 ShiroBot / Avalonia 类型引用以支撑 contract。
- 不要在共享层读取本地文件、启动后台任务或持有长期资源。

## 构建

单独构建共享层：

```bash
dotnet build MyParser.Sharing/MyParser.Sharing.csproj -c Release
```

通常建议直接构建整个 solution：

```bash
dotnet build MyParser.sln -c Release -p:CopyPluginToHost=false -p:BuildMyParserProvidersOnMainPluginBuild=false
```
