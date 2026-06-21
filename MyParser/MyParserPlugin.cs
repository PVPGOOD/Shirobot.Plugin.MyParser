using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Shirobot.Plugin.MyParser.MessageHandling;
using Shirobot.Plugin.MyParser.Parsing;
using Shirobot.Plugin.MyParser.Utility;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace Shirobot.Plugin.MyParser;

[BotPlugin(id: "MyParser",
    Name = "MyParser",
    Version = "0.2.0",
    Author = "PVPGood",
    Category = PluginCategory.Utility,
    Description = "面向 Shirobot 的学习型内容消息处理插件。",
    GithubRepo = "PVPGOOD/Shirobot.Plugin.MyParser",
    IsPluginSingleFile = false)
]
public sealed class MyParserPlugin : PluginBase
{
    private static readonly object ProviderAssemblyResolverLock = new();
    private static bool providerAssemblyResolverRegistered;
    private static string? providerAssemblyResolverPluginDirectory;
    private static AssemblyLoadContext? providerAssemblyLoadContext;

    private const string CookieDirectoryName = "cookies";
    private const string DouyinCookieFileName = "douyin.txt";
    private const string BilibiliCookieFileName = "bilibili.txt";
    private const string XiaohongshuCookieFileName = "xiaohongshu.txt";
    private const string BilibiliLoginCommand = "#bili-login";
    private const string XiaohongshuLoginCommand = "#xhs-login";
    private const string DouyinCookieCheckCommand = "#douyin-cookie-check";
    private const string BilibiliCookieCheckCommand = "#bili-cookie-check";
    private const string XiaohongshuCookieCheckCommand = "#xhs-cookie-check";

    private readonly Lock _reloadLock = new();
    private PluginConfig _config = new();
    private FileSystemWatcher? _configWatcher;
    private FileSystemWatcher? _cookieWatcher;
    private CancellationTokenSource? _configReloadDebounce;
    private CancellationTokenSource? _cookieReloadDebounce;
    private readonly List<IParseProvider> _providers = [];
    private readonly List<IDisposable> _providerDisposables = [];
    private IParseProvider? _douyinProvider;
    private IParseProvider? _bilibiliProvider;
    private IParseProvider? _xiaohongshuProvider;
    private IIncomingProviderTextNormalizer? _bilibiliTextNormalizer;
    private ICookieValidator? _douyinCookieValidator;
    private ICookieValidator? _bilibiliCookieValidator;
    private ParseProviderRegistry? _providerRegistry;
    private ProviderHostServices? _hostServices;
    private IProviderMessageHandler? _douyinMessageHandler;
    private IProviderMessageHandler? _bilibiliMessageHandler;
    private IProviderMessageHandler? _xiaohongshuMessageHandler;

    public override string Name => "MyParser";

    protected override async Task LoadAsync()
    {
        MyParserRuntime.ResetForLoad();
        ProviderMessageUtilities.ClearReactionCache();
        _config = Context.Config.Load<PluginConfig>();
        _hostServices = new ProviderHostServices(Context);
        var modules = DiscoverProviderModules(GetPluginDirectory());
        _bilibiliTextNormalizer = modules.OfType<IIncomingProviderTextNormalizer>().FirstOrDefault(i => string.Equals(((IMyParserProviderModule)i).Id, "bilibili", StringComparison.OrdinalIgnoreCase));
        _douyinCookieValidator = modules.OfType<ICookieValidator>().FirstOrDefault(i => string.Equals(((IMyParserProviderModule)i).Id, "douyin", StringComparison.OrdinalIgnoreCase));
        _bilibiliCookieValidator = modules.OfType<ICookieValidator>().FirstOrDefault(i => string.Equals(((IMyParserProviderModule)i).Id, "bilibili", StringComparison.OrdinalIgnoreCase));
        NormalizeRuntimeDirectories();
        LoadDouyinCookieFromPluginDirectory();
        LoadBilibiliCookieFromPluginDirectory();
        LoadXiaohongshuCookieFromPluginDirectory();

        foreach (var module in modules)
        {
            foreach (var provider in module.CreateProviders(_config))
            {
                _providers.Add(provider);
                if (provider is IDisposable disposable)
                {
                    _providerDisposables.Add(disposable);
                }
            }
        }

        var orderedProviders = _providers.OrderBy(GetProviderOrder).ToArray();
        _douyinProvider = orderedProviders.FirstOrDefault(i => string.Equals(i.Id, "douyin", StringComparison.OrdinalIgnoreCase));
        _bilibiliProvider = orderedProviders.FirstOrDefault(i => string.Equals(i.Id, "bilibili", StringComparison.OrdinalIgnoreCase));
        _xiaohongshuProvider = orderedProviders.FirstOrDefault(i => string.Equals(i.Id, "xiaohongshu", StringComparison.OrdinalIgnoreCase));
        _providerRegistry = new ParseProviderRegistry(orderedProviders);
        _douyinMessageHandler = CreateProviderMessageHandler(modules, _douyinProvider);
        _bilibiliMessageHandler = CreateProviderMessageHandler(modules, _bilibiliProvider);
        _xiaohongshuMessageHandler = CreateProviderMessageHandler(modules, _xiaohongshuProvider);

        LogLoadedProviderCapabilities(modules, orderedProviders);

        await LogDouyinCookieLoginStatusAsync();
        await LogBilibiliCookieLoginStatusAsync();
        await LogXiaohongshuCookieLoginStatusAsync();

        // GroupCommands.MapExact("b", async message =>
        // {
        //     BotLog.Error("MyParser 错误日志测试");
        //     var text = GetPlainText(message);
        //     var pic = await Context.RenderControlPngAsync<BiliCard>(new BiliCardViewModel(),
        //         new ControlRenderOptions(RenderTheme.Light,192));
        //     await Context.Message.ReplyAsync(message, $"b卡",new ImageOutgoingSegment($"base64://{Convert.ToBase64String(pic)}"));
        // });
        //
        // GroupCommands.MapExact("d", async message =>
        // {
        //     BotLog.Error("MyParser 错误日志测试");
        //     var text = GetPlainText(message);
        //     var pic = await Context.RenderControlPngAsync<DouyinCard>(new DouyinCardViewModel(),
        //         new ControlRenderOptions(RenderTheme.Light));
        //     await Context.Message.ReplyAsync(message, $"dycard",new ImageOutgoingSegment($"base64://{Convert.ToBase64String(pic)}"));
        // });
        //
        FriendCommands.MapExact("#parser", HandleHelpAsync);
        GroupCommands.MapExact("#parser", HandleHelpAsync);
        FriendCommands.MapExact(BilibiliLoginCommand, HandleBilibiliLoginAsync);
        GroupCommands.MapExact(BilibiliLoginCommand, HandleBilibiliLoginAsync);
        FriendCommands.MapExact(XiaohongshuLoginCommand, HandleXiaohongshuLoginAsync);
        GroupCommands.MapExact(XiaohongshuLoginCommand, HandleXiaohongshuLoginAsync);
        FriendCommands.MapExact(DouyinCookieCheckCommand, HandleDouyinCookieCheckAsync);
        GroupCommands.MapExact(DouyinCookieCheckCommand, HandleDouyinCookieCheckAsync);
        FriendCommands.MapExact(BilibiliCookieCheckCommand, HandleBilibiliCookieCheckAsync);
        GroupCommands.MapExact(BilibiliCookieCheckCommand, HandleBilibiliCookieCheckAsync);
        FriendCommands.MapExact(XiaohongshuCookieCheckCommand, HandleXiaohongshuCookieCheckAsync);
        GroupCommands.MapExact(XiaohongshuCookieCheckCommand, HandleXiaohongshuCookieCheckAsync);

        FriendCommands.MapWhen(IsParseCommand, HandleParseCommandAsync);
        GroupCommands.MapWhen(IsParseCommand, HandleParseCommandAsync);

        FriendCommands.MapWhen(ShouldAutoParse, HandleAutoParseAsync);
        GroupCommands.MapWhen(ShouldAutoParse, HandleAutoParseAsync);

        StartHotReloadWatchers();
        BotLog.Info($"MyParser 已加载：抖音/Bilibili/小红书 解析启用。命令：#parser / {_config.ParseCommandPrefix} <链接> / {BilibiliLoginCommand} / {XiaohongshuLoginCommand}");
    }

    private static IMyParserProviderModule[] DiscoverProviderModules(string pluginDirectory)
    {
        EnsureProviderAssemblyResolver(pluginDirectory);
        LoadProviderAssemblies(pluginDirectory);

        var contractAssemblyName = typeof(IMyParserProviderModule).Assembly.FullName;
        BotLog.Info($"MyParser provider contract assembly：{contractAssemblyName}");

        var modules = new List<IMyParserProviderModule>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(i => i.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
                var loaderErrors = string.Join("; ", ex.LoaderExceptions.Select(i => i?.Message).Where(i => !string.IsNullOrWhiteSpace(i)).Take(3));
                if (assembly.GetName().Name?.StartsWith("myparser-provider-", StringComparison.OrdinalIgnoreCase) == true)
                {
                    BotLog.Warning($"MyParser provider 类型加载不完整：{assembly.FullName}, errors={loaderErrors}");
                }
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || typeof(IMyParserProviderModule).IsAssignableFrom(type) is false)
                {
                    continue;
                }

                BotLog.Info($"MyParser 发现 provider module 类型：{type.FullName}, assembly={assembly.FullName}");
                modules.Add((IMyParserProviderModule)Activator.CreateInstance(type)!);
            }

            if (assembly.GetName().Name?.StartsWith("myparser-provider-", StringComparison.OrdinalIgnoreCase) == true
                && !types.Any(type => !type.IsAbstract && typeof(IMyParserProviderModule).IsAssignableFrom(type)))
            {
                var candidates = types
                    .Where(type => !type.IsAbstract && type.GetInterfaces().Any(i => string.Equals(i.FullName, typeof(IMyParserProviderModule).FullName, StringComparison.Ordinal)))
                    .Select(type => type.FullName)
                    .ToArray();
                var candidateText = candidates.Length == 0 ? "none" : string.Join(", ", candidates);
                BotLog.Warning($"MyParser provider assembly 中未发现可赋值 module：{assembly.FullName}, contract={contractAssemblyName}, same-name-candidates={candidateText}");
            }
        }

        return modules
            .OrderBy(module => module.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssemblyLoadContext GetProviderAssemblyLoadContext()
    {
        return AssemblyLoadContext.GetLoadContext(typeof(MyParserPlugin).Assembly)
               ?? AssemblyLoadContext.Default;
    }

    private static void EnsureProviderAssemblyResolver(string pluginDirectory)
    {
        lock (ProviderAssemblyResolverLock)
        {
            providerAssemblyResolverPluginDirectory = pluginDirectory;
            var loadContext = GetProviderAssemblyLoadContext();
            providerAssemblyLoadContext = loadContext;
            if (providerAssemblyResolverRegistered)
            {
                return;
            }

            loadContext.Resolving += ResolveProviderAssembly;
            providerAssemblyResolverRegistered = true;
            BotLog.Info($"MyParser provider load context：{loadContext.GetType().FullName}");
        }
    }

    private static Assembly? ResolveProviderAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)
                                        && AssemblyLoadContext.GetLoadContext(assembly) == context);
        if (loaded is not null)
        {
            return loaded;
        }

        var pluginDirectory = providerAssemblyResolverPluginDirectory;
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return null;
        }

        var fileName = assemblyName.Name + ".dll";
        var candidates = new[]
        {
            Path.Combine(pluginDirectory, fileName),
            Path.Combine(pluginDirectory, "provider", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return context.LoadFromAssemblyPath(Path.GetFullPath(candidate));
            }
            catch (Exception ex)
            {
                BotLog.Warning($"MyParser provider 依赖加载失败：{candidate}, error={ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    private static void LoadProviderAssemblies(string pluginDirectory)
    {
        var providerDirectory = Path.Combine(pluginDirectory, "provider");
        if (!Directory.Exists(providerDirectory))
        {
            BotLog.Warning($"MyParser provider 目录不存在：{providerDirectory}");
            return;
        }

        var loadedPaths = AppDomain.CurrentDomain.GetAssemblies()
            .Where(i => !string.IsNullOrWhiteSpace(i.Location))
            .Select(i => Path.GetFullPath(i.Location))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var providerAssembly in Directory.EnumerateFiles(providerDirectory, "myparser-provider-*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(providerAssembly);
            if (loadedPaths.Contains(fullPath))
            {
                continue;
            }

            try
            {
                var loadContext = providerAssemblyLoadContext ?? GetProviderAssemblyLoadContext();
                var assembly = loadContext.LoadFromAssemblyPath(fullPath);
                BotLog.Info($"MyParser 已加载 provider：{Path.GetFileName(fullPath)}, assembly={assembly.FullName}, loadContext={loadContext.GetType().FullName}");
            }
            catch (Exception ex)
            {
                BotLog.Warning($"MyParser provider 加载失败：{fullPath}, error={ex.GetType().Name}: {ex.Message}");
            }
        }
    }


    private static void LogLoadedProviderCapabilities(IReadOnlyCollection<IMyParserProviderModule> modules, IReadOnlyCollection<IParseProvider> providers)
    {
        if (modules.Count == 0)
        {
            BotLog.Warning("MyParser 未发现任何 provider module。请检查 plugins/Shirobot.Plugin.MyParser/provider 下是否存在 myparser-provider-*.dll。");
            return;
        }

        BotLog.Info($"MyParser provider 能力概览：modules={modules.Count}, providers={providers.Count}");

        foreach (var module in modules.OrderBy(module => module.Id, StringComparer.OrdinalIgnoreCase))
        {
            var moduleProviders = providers
                .Where(provider => string.Equals(GetModuleId(provider.Id), module.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(GetProviderOrder)
                .ThenBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var moduleCapabilities = GetModuleCapabilities(module);
            BotLog.Info($"MyParser provider module：id={module.Id}, type={module.GetType().FullName}, capabilities=[{string.Join(", ", moduleCapabilities)}]");

            if (moduleProviders.Length == 0)
            {
                BotLog.Warning($"MyParser provider module 未注册解析器：id={module.Id}");
                continue;
            }

            foreach (var provider in moduleProviders)
            {
                var providerCapabilities = GetProviderCapabilities(provider);
                BotLog.Info($"MyParser provider：id={provider.Id}, name={provider.Name}, type={provider.GetType().FullName}, capabilities=[{string.Join(", ", providerCapabilities)}]");
            }
        }
    }

    private static IReadOnlyList<string> GetModuleCapabilities(IMyParserProviderModule module)
    {
        var capabilities = new List<string> { "url-parse" };

        if (module is IProviderMessageHandlerFactory)
        {
            capabilities.Add("message-handler");
        }

        if (module is IProviderTextNormalizer)
        {
            capabilities.Add("text-normalizer");
        }

        if (module is IIncomingProviderTextNormalizer)
        {
            capabilities.Add("incoming-normalizer");
        }

        if (module is ICookieValidator)
        {
            capabilities.Add("cookie-validator");
        }

        if (module is IProviderRuntimeModule)
        {
            capabilities.Add("runtime-module");
        }

        if (module is IProviderReplyParseTextBuilder)
        {
            capabilities.Add("reply-parse-text-builder");
        }

        return capabilities;
    }

    private static IReadOnlyList<string> GetProviderCapabilities(IParseProvider provider)
    {
        var capabilities = new List<string> { "url-recognition", "content-parse" };

        if (provider is IIncomingMessageParseProvider)
        {
            capabilities.Add("incoming-message-extract");
        }

        if (provider is IProviderLoginStatusProvider)
        {
            capabilities.Add("login-status");
        }

        if (provider is IQrLoginProvider)
        {
            capabilities.Add("qr-login");
        }

        if (provider is IParserHttpClientAccessor)
        {
            capabilities.Add("http-client");
        }

        if (provider is IParseProviderWithParser { ParserObject: IParserHttpClientAccessor })
        {
            capabilities.Add("parser-http-client");
        }

        if (provider is IVideoDownloadGate || provider is IParseProviderWithParser { ParserObject: IVideoDownloadGate })
        {
            capabilities.Add("video-download-gate");
        }

        if (provider is IDisposable)
        {
            capabilities.Add("disposable");
        }

        return capabilities;
    }

    private IProviderMessageHandler? CreateProviderMessageHandler(IEnumerable<IMyParserProviderModule> modules, IParseProvider? provider)
    {
        if (provider is null || _providerRegistry is null)
        {
            return null;
        }

        var module = modules
            .OfType<IProviderMessageHandlerFactory>()
            .FirstOrDefault(i => string.Equals(((IMyParserProviderModule)i).Id, GetModuleId(provider.Id), StringComparison.OrdinalIgnoreCase));
        return _hostServices is null
            ? null
            : module?.CreateMessageHandler(new ProviderMessageHandlerContext(Context, _config, _providerRegistry, provider, _hostServices));
    }

    private static string GetModuleId(string providerId)
    {
        return providerId.StartsWith("bilibili", StringComparison.OrdinalIgnoreCase)
            ? "bilibili"
            : providerId;
    }

    private static int GetProviderOrder(IParseProvider provider)
    {
        return provider.Id switch
        {
            "xiaohongshu" => 0,
            "douyin" => 1,
            "bilibili-article" => 2,
            "bilibili-bangumi" => 3,
            "bilibili-live" => 4,
            "bilibili" => 5,
            _ => 100,
        };
    }

    private Task LogDouyinCookieLoginStatusAsync() => LogProviderCookieLoginStatusAsync(_douyinProvider, "Douyin");

    private Task LogBilibiliCookieLoginStatusAsync() => LogProviderCookieLoginStatusAsync(_bilibiliProvider, "Bilibili");

    private Task LogXiaohongshuCookieLoginStatusAsync() => LogProviderCookieLoginStatusAsync(_xiaohongshuProvider, "Xiaohongshu");

    private static async Task LogProviderCookieLoginStatusAsync(IParseProvider? provider, string platformName)
    {
        if (provider is not IProviderLoginStatusProvider loginStatusProvider)
        {
            return;
        }

        try
        {
            var status = await loginStatusProvider.CheckLoginStatusAsync();
            BotLog.Info($"MyParser {platformName}Cookie 登录状态：{status.Message}");
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser {platformName}Cookie 登录状态检查失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadDouyinCookieFromPluginDirectory()
    {
        var cookiePath = ResolveCookiePath(DouyinCookieFileName);

        if (!File.Exists(cookiePath))
        {
            MyParserRuntime.DouyinCookie = string.Empty;
            File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
            BotLog.Info($"MyParser 已创建抖音 Cookie 文件：{cookiePath}");
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            MyParserRuntime.DouyinCookie = string.Empty;
            BotLog.Info($"MyParser DouyinCookie 为空；可编辑文件后重启：{cookiePath}");
            return;
        }

        if (_douyinCookieValidator is not null && !_douyinCookieValidator.LooksLikeCookie(cookie))
        {
            MyParserRuntime.DouyinCookie = string.Empty;
            BotLog.Warning($"MyParser 忽略无效 DouyinCookie 文件：{cookiePath}。请确保文件内容是浏览器 Request Headers 中 Cookie: 后面的完整值。");
            return;
        }

        MyParserRuntime.DouyinCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 DouyinCookie：{cookiePath}");
    }

    private void LoadBilibiliCookieFromPluginDirectory()
    {
        var cookiePath = ResolveCookiePath(BilibiliCookieFileName);

        if (!File.Exists(cookiePath))
        {
            MyParserRuntime.BilibiliCookie = string.Empty;
            File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
            BotLog.Info($"MyParser 已创建 Bilibili Cookie 文件：{cookiePath}");
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            MyParserRuntime.BilibiliCookie = string.Empty;
            BotLog.Info($"MyParser BilibiliCookie 为空；可发送 {BilibiliLoginCommand} 扫码登录，或编辑文件后重启：{cookiePath}");
            return;
        }

        if (_bilibiliCookieValidator is not null && !_bilibiliCookieValidator.LooksLikeCookie(cookie))
        {
            MyParserRuntime.BilibiliCookie = string.Empty;
            BotLog.Warning($"MyParser 忽略无效 BilibiliCookie 文件：{cookiePath}。请确保文件内容包含 SESSDATA/bili_jct 等 Cookie。");
            return;
        }

        MyParserRuntime.BilibiliCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 BilibiliCookie：{cookiePath}");
    }

    private void LoadXiaohongshuCookieFromPluginDirectory()
    {
        var cookiePath = ResolveCookiePath(XiaohongshuCookieFileName);

        if (!File.Exists(cookiePath))
        {
            MyParserRuntime.XiaohongshuCookie = string.Empty;
            File.WriteAllText(cookiePath, string.Empty, Encoding.UTF8);
            BotLog.Info($"MyParser 已创建小红书 Cookie 文件：{cookiePath}");
            return;
        }

        var cookie = File.ReadAllText(cookiePath, Encoding.UTF8).Trim().TrimStart('\ufeff');
        if (string.IsNullOrWhiteSpace(cookie))
        {
            MyParserRuntime.XiaohongshuCookie = string.Empty;
            BotLog.Info($"MyParser XiaohongshuCookie 为空；可发送 {XiaohongshuLoginCommand} 扫码登录，或编辑文件后重启：{cookiePath}");
            return;
        }

        MyParserRuntime.XiaohongshuCookie = cookie;
        BotLog.Info($"MyParser 已从插件目录读取 XiaohongshuCookie：{cookiePath}");
    }

    private void NormalizeRuntimeDirectories()
    {
        var pluginDir = GetPluginDirectory();
        Directory.CreateDirectory(pluginDir);

        MyParserRuntime.DouyinCookie = string.Empty;
        MyParserRuntime.BilibiliCookie = string.Empty;
        MyParserRuntime.XiaohongshuCookie = string.Empty;

        MyParserRuntime.DownloadDirectory = Path.Combine(pluginDir, "tmp", "douyin");
        MyParserRuntime.BilibiliDownloadDirectory = Path.Combine(pluginDir, "tmp", "bilibili");
        MyParserRuntime.XiaohongshuDownloadDirectory = Path.Combine(pluginDir, "tmp", "xiaohongshu");

        Directory.CreateDirectory(Path.Combine(pluginDir, CookieDirectoryName));
        Directory.CreateDirectory(MyParserRuntime.DownloadDirectory);
        Directory.CreateDirectory(MyParserRuntime.BilibiliDownloadDirectory);
        Directory.CreateDirectory(MyParserRuntime.XiaohongshuDownloadDirectory);
        LocalMediaCleanup.CleanupStartupResidues(_config);
    }

    private string ResolveCookiePath(string fileName)
    {
        return ResolveCookiePath(GetPluginDirectory(), fileName);
    }

    internal static string ResolveCookiePath(string pluginDirectory, string fileName)
    {
        var cookieDir = Path.Combine(pluginDirectory, CookieDirectoryName);
        Directory.CreateDirectory(cookieDir);
        return Path.Combine(cookieDir, Path.GetFileName(fileName));
    }

    private void StartHotReloadWatchers()
    {
        StartConfigHotReloadWatcher();
        StartCookieHotReloadWatcher();
    }

    private void StartConfigHotReloadWatcher()
    {
        var configPath = Context.Config.ConfigPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(fullPath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        _configWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _configWatcher.Changed += (_, _) => ScheduleConfigReload();
        _configWatcher.Created += (_, _) => ScheduleConfigReload();
        _configWatcher.Renamed += (_, _) => ScheduleConfigReload();
        BotLog.Info($"MyParser 配置热重载已启用：{fullPath}");
    }

    private void StartCookieHotReloadWatcher()
    {
        var cookieDir = Path.Combine(GetPluginDirectory(), CookieDirectoryName);
        Directory.CreateDirectory(cookieDir);
        _cookieWatcher = new FileSystemWatcher(cookieDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _cookieWatcher.Changed += (_, _) => ScheduleCookieReload();
        _cookieWatcher.Created += (_, _) => ScheduleCookieReload();
        _cookieWatcher.Deleted += (_, _) => ScheduleCookieReload();
        _cookieWatcher.Renamed += (_, _) => ScheduleCookieReload();
        BotLog.Info($"MyParser Cookie 热重载已启用：{cookieDir}");
    }

    private void ScheduleConfigReload()
    {
        ScheduleDebouncedReload(ref _configReloadDebounce, ReloadConfigNow, "配置");
    }

    private void ScheduleCookieReload()
    {
        ScheduleDebouncedReload(ref _cookieReloadDebounce, ReloadCookiesNow, "Cookie");
    }

    private void ScheduleDebouncedReload(ref CancellationTokenSource? debounce, Action reloadAction, string name)
    {
        lock (_reloadLock)
        {
            debounce?.Cancel();
            debounce?.Dispose();
            debounce = new CancellationTokenSource();
            var token = debounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    reloadAction();
                }
                catch (OperationCanceledException)
                {
                    // debounce
                }
                catch (Exception ex)
                {
                    BotLog.Warning($"MyParser {name}热重载失败：{ex.GetType().Name}: {ex.Message}");
                }
            }, CancellationToken.None);
        }
    }

    private void ReloadConfigNow()
    {
        var updated = Context.Config.Load<PluginConfig>();
        ApplyConfigValues(_config, updated);
        BotLog.Info("MyParser 配置已热重载。注意：命令热重载仅支持 #parse 前缀；登录/Cookie 检查命令为固定命令。 ");
    }

    private void ReloadCookiesNow()
    {
        LoadDouyinCookieFromPluginDirectory();
        LoadBilibiliCookieFromPluginDirectory();
        LoadXiaohongshuCookieFromPluginDirectory();
        BotLog.Info("MyParser Cookie 文件已热重载。 ");
    }

    private static void ApplyConfigValues(PluginConfig target, PluginConfig source)
    {
        foreach (var property in typeof(PluginConfig).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(target, property.GetValue(source));
        }
    }

    private string GetPluginDirectory()
    {
        return string.IsNullOrWhiteSpace(Context.PluginDirectory)
            ? Path.GetDirectoryName(Context.Config.ConfigPath) ?? AppContext.BaseDirectory
            : Context.PluginDirectory;
    }

    protected override Task OnUnloadAsync()
    {
        MyParserRuntime.BeginUnload();
        ProviderMessageUtilities.ClearReactionCache();
        _configReloadDebounce?.Cancel();
        _configReloadDebounce?.Dispose();
        _configReloadDebounce = null;
        _cookieReloadDebounce?.Cancel();
        _cookieReloadDebounce?.Dispose();
        _cookieReloadDebounce = null;
        _configWatcher?.Dispose();
        _configWatcher = null;
        _cookieWatcher?.Dispose();
        _cookieWatcher = null;

        _hostServices?.Dispose();
        _hostServices = null;

        _douyinMessageHandler?.Dispose();
        _douyinMessageHandler = null;
        _bilibiliMessageHandler?.Dispose();
        _bilibiliMessageHandler = null;
        _xiaohongshuMessageHandler?.Dispose();
        _xiaohongshuMessageHandler = null;
        foreach (var disposable in _providerDisposables)
        {
            disposable.Dispose();
        }

        _providerDisposables.Clear();
        _providers.Clear();
        _douyinProvider = null;
        _bilibiliProvider = null;
        _xiaohongshuProvider = null;
        _bilibiliTextNormalizer = null;
        _douyinCookieValidator = null;
        _bilibiliCookieValidator = null;
        _providerRegistry = null;
        BotLog.Info("MyParser 已卸载。");
        return Task.CompletedTask;
    }

    private Task HandleHelpAsync(IncomingMessage message)
    {
        var help = "MyParser\n"
                   + "当前支持：抖音视频 / 图集 / LivePhoto、Bilibili 视频/专栏/图文、小红书视频/图文/评论卡片\n\n"
                   + "用法：\n"
                   + $"1. {_config.ParseCommandPrefix} <抖音/Bilibili/小红书 分享链接>\n"
                   + "2. 直接发送抖音/Bilibili/小红书链接可自动解析\n"
                   + $"3. {BilibiliLoginCommand}：Bilibili 扫码登录并保存 Cookie\n"
                   + $"4. {XiaohongshuLoginCommand}：小红书扫码登录并保存 Cookie\n"
                   + $"5. {DouyinCookieCheckCommand} / {BilibiliCookieCheckCommand} / {XiaohongshuCookieCheckCommand}：检查 Cookie 有效性\n\n"
                   + "Cookie 文件：插件目录/cookies/douyin.txt、cookies/bilibili.txt、cookies/xiaohongshu.txt\n"
                   + "Bilibili 说明：需要登录态；视频/音频流会分别下载，并用本地 ffmpeg 合并后发送。\n"
                   + "小红书说明：需要自行搭建 xhshow sign 服务，并在运行时配置 sign URL/token。";
        return Context.Message.ReplyAsync(message, help);
    }

    private bool IsParseCommand(IncomingMessage message)
    {
        var text = GetPlainText(message).TrimStart();
        return !string.IsNullOrWhiteSpace(_config.ParseCommandPrefix)
               && text.StartsWith(_config.ParseCommandPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldAutoParse(IncomingMessage message)
    {
        if (TryBuildBilibiliPageLinkFromReply(message, out _))
        {
            return _config.AutoParseBilibiliLinks;
        }

        var text = GetPlainText(message);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith(_config.ParseCommandPrefix, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("#parser", StringComparison.OrdinalIgnoreCase)
                || IsPluginResultMessage(trimmed)
                || IsBilibiliPageTemplateLink(trimmed)
                || trimmed.StartsWith(BilibiliLoginCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(XiaohongshuLoginCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(DouyinCookieCheckCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(BilibiliCookieCheckCommand, StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith(XiaohongshuCookieCheckCommand, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var parseText = GetStrictAutoParseText(message);
        var provider = string.IsNullOrWhiteSpace(parseText) ? null : _providerRegistry?.FindProvider(parseText);
        if (provider is null && _providerRegistry?.FindProvider(message, out _) is { } fallbackProvider && !IsBilibiliProvider(fallbackProvider.Id))
        {
            provider = fallbackProvider;
        }
        return provider?.Id switch
        {
            "douyin" => _config.AutoParseDouyinLinks,
            "bilibili" => _config.AutoParseBilibiliLinks,
            "bilibili-article" => _config.AutoParseBilibiliLinks,
            "bilibili-bangumi" => _config.AutoParseBilibiliLinks,
            "bilibili-live" => _config.AutoParseBilibiliLinks,
            "xiaohongshu" => _config.AutoParseXiaohongshuLinks,
            _ => false,
        };
    }

    private Task HandleAutoParseAsync(IncomingMessage message)
    {
        if (TryBuildBilibiliPageLinkFromReply(message, out var pageLink))
        {
            return DispatchParseAsync(message, pageLink, silentProviderMismatch: true);
        }

        var parseText = GetStrictAutoParseText(message);
        if (!string.IsNullOrWhiteSpace(parseText) && _providerRegistry?.FindProvider(parseText) is not null)
        {
            return DispatchParseAsync(message, parseText, silentProviderMismatch: true);
        }

        if (_providerRegistry?.FindProvider(message, out parseText) is { } fallbackProvider && !IsBilibiliProvider(fallbackProvider.Id) && !string.IsNullOrWhiteSpace(parseText))
        {
            return DispatchParseAsync(message, parseText, silentProviderMismatch: true);
        }

        return Task.CompletedTask;
    }

    private static bool IsBilibiliProvider(string providerId)
    {
        return providerId.StartsWith("bilibili", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetStrictAutoParseText(IncomingMessage message)
    {
        return _bilibiliTextNormalizer?.NormalizeParseText(message);
    }

    private Task HandleParseCommandAsync(IncomingMessage message)
    {
        var text = GetPlainText(message);
        var content = text.Length <= _config.ParseCommandPrefix.Length
            ? string.Empty
            : text[_config.ParseCommandPrefix.Length..].Trim();

        return DispatchParseAsync(message, string.IsNullOrWhiteSpace(content) ? text : content);
    }

    private async Task HandleBilibiliLoginAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, BilibiliLoginCommand))
        {
            return;
        }

        if (_bilibiliMessageHandler is not null)
        {
            await _bilibiliMessageHandler.HandleLoginAsync(message);
        }
    }

    private async Task HandleXiaohongshuLoginAsync(IncomingMessage message)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, XiaohongshuLoginCommand))
        {
            return;
        }

        if (_xiaohongshuMessageHandler is not null)
        {
            await _xiaohongshuMessageHandler.HandleLoginAsync(message);
        }
    }

    private Task HandleDouyinCookieCheckAsync(IncomingMessage message)
    {
        return HandleProviderCookieCheckAsync(message, DouyinCookieCheckCommand, _douyinProvider, "Douyin");
    }

    private Task HandleBilibiliCookieCheckAsync(IncomingMessage message)
    {
        return HandleProviderCookieCheckAsync(message, BilibiliCookieCheckCommand, _bilibiliProvider, "Bilibili");
    }

    private Task HandleXiaohongshuCookieCheckAsync(IncomingMessage message)
    {
        return HandleProviderCookieCheckAsync(message, XiaohongshuCookieCheckCommand, _xiaohongshuProvider, "Xiaohongshu");
    }

    private async Task HandleProviderCookieCheckAsync(IncomingMessage message, string command, IParseProvider? provider, string platformName)
    {
        if (!await EnsurePrivateAdminCommandAsync(message, command))
        {
            return;
        }

        if (provider is not IProviderLoginStatusProvider loginStatusProvider)
        {
            await Context.Message.ReplyAsync(message, $"{platformName} 解析器尚未初始化或不支持 Cookie 检查。");
            return;
        }

        try
        {
            var status = await loginStatusProvider.CheckLoginStatusAsync();
            var detail = status.IsLogin
                ? $"有效 / 已登录：{status.UserName ?? "未知用户"}" + (string.IsNullOrWhiteSpace(status.UserId) ? string.Empty : $" ({status.UserId})")
                : status.NeedVerify ? "触发安全验证：" + status.Message : "无效 / 未登录：" + status.Message;
            await Context.Message.ReplyAsync(message, $"{platformName}Cookie 状态：" + detail);
        }
        catch (Exception ex)
        {
            BotLog.Warning($"MyParser {platformName}Cookie 手动检查失败：{ex}");
            await Context.Message.ReplyAsync(message, $"{platformName}Cookie 状态检查失败：" + ex.Message);
        }
    }

    private async Task<bool> EnsurePrivateAdminCommandAsync(IncomingMessage message, string command)
    {
        switch (message)
        {
            case FriendIncomingMessage friend when Context.IsAdmin(friend.SenderId):
                return true;
            case FriendIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用。");
                return false;
            case GroupIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 涉及账号登录凭据，仅允许机器人 Owner/Admin 私信机器人使用，请不要在群内触发。");
                return false;
            case TempIncomingMessage:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用，不支持临时会话。");
                return false;
            default:
                await Context.Message.ReplyAsync(message, $"{command} 仅允许机器人 Owner/Admin 私信使用。");
                return false;
        }
    }

    private static bool TryBuildBilibiliPageLinkFromReply(IncomingMessage message, out string pageLink)
    {
        pageLink = string.Empty;
        var text = GetPlainText(message).Trim();
        if (!int.TryParse(text, out var page) || page <= 0)
        {
            return false;
        }

        var reply = message switch
        {
            GroupIncomingMessage group => group.GetReply(),
            FriendIncomingMessage friend => friend.GetReply(),
            _ => null,
        };
        if (reply is null)
        {
            return false;
        }

        var repliedText = string.Concat(reply.Segments.OfType<TextIncomingSegment>().Select(i => i.Text)).Trim();
        if (!IsBilibiliPageTemplateLink(repliedText))
        {
            return false;
        }

        pageLink = repliedText + page;
        return true;
    }

    private static bool IsBilibiliPageTemplateLink(string text)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            text.Trim(),
            @"^https?://www\.bilibili\.com/video/BV[0-9A-Za-z]{10}/?\?p=\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsPluginResultMessage(string text)
    {
        return text.StartsWith("Bilibili 视频解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 直播解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 图文解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Bilibili 专栏解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Douyin 解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("抖音解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("小红书解析", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Xiaohongshu", StringComparison.OrdinalIgnoreCase);
    }

    private Task DispatchParseAsync(IncomingMessage message, string text, bool silentProviderMismatch = false)
    {
        text = _bilibiliTextNormalizer?.NormalizeParseText(text) ?? text;
        if (IsBilibiliPageTemplateLink(text))
        {
            return Task.CompletedTask;
        }

        var provider = _providerRegistry?.FindProvider(text);
        return provider?.Id switch
        {
            "douyin" => _douyinMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            "bilibili" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            "bilibili-article" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            "bilibili-bangumi" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            "bilibili-live" => _bilibiliMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            "xiaohongshu" => _xiaohongshuMessageHandler?.ParseAndReplyAsync(message, text, silentProviderMismatch) ?? Task.CompletedTask,
            _ => Context.Message.ReplyAsync(message, "未找到可处理该链接的解析提供商。"),
        };
    }

    private static string GetPlainText(IncomingMessage message) => message switch
    {
        FriendIncomingMessage friend => friend.GetPlainText(),
        GroupIncomingMessage group => group.GetPlainText(),
        TempIncomingMessage temp => string.Concat(temp.Segments.OfType<TextIncomingSegment>().Select(i => i.Text)),
        _ => string.Empty,
    };
}

