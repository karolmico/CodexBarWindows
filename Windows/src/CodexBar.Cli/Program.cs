using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using CodexBar.Core.Cli;
using CodexBar.Core.Providers;
using CodexBar.Core.Settings;
using CodexBar.Providers.Windows;

namespace CodexBar.Cli;

/// <summary>
/// Headless entry point. All shaping lives in <c>CodexBar.Core.Cli</c>; this file only wires the real
/// provider catalog, settings file, and console streams.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

    private static async Task<int> Main(string[] args)
    {
        var invocation = CliArgumentParser.Parse(args);
        if (invocation.Error is { } error)
        {
            Console.Error.WriteLine($"codexbar: {error}");
            Console.Error.WriteLine(CliHelp.Usage);
            return (int)CliExitCode.UsageError;
        }

        var version = Version();
        switch (invocation.Command)
        {
            case CliCommand.Help:
                Console.Out.WriteLine(CliHelp.Usage);
                return (int)CliExitCode.Success;
            case CliCommand.Version:
                Console.Out.WriteLine(CliHelp.Version(version));
                return (int)CliExitCode.Success;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var registry = ProviderCatalog.Create(httpClient);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexBar");
        var settingsStore = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        var settings = settingsStore.Load(
            AppSettings.DefaultFor(registry.All.Select(provider => provider.Descriptor))).Settings;

        IReadOnlyList<IUsageProvider> selected;
        if (invocation.Providers.Count > 0)
        {
            var selectedProviders = new List<IUsageProvider>();
            foreach (var key in invocation.Providers)
            {
                if (!registry.TryGet(key, out var provider))
                {
                    Console.Error.WriteLine($"codexbar: unknown provider '{key}'.");
                    Console.Error.WriteLine($"Known providers: {string.Join(", ", registry.All.Select(p => p.Descriptor.Key.Value))}");
                    return (int)CliExitCode.UsageError;
                }

                selectedProviders.Add(provider);
            }

            selected = selectedProviders;
        }
        else
        {
            selected = registry.Resolve(settings.EnabledProviderKeys);
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine("codexbar: no providers selected. Enable one in settings or pass --provider.");
            return (int)CliExitCode.TotalFailure;
        }

        using var timeout = new CancellationTokenSource(FetchTimeout);
        IReadOnlyList<CliProviderResult> results;
        try
        {
            results = await new CliUsageRunner().RunAsync(selected, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"codexbar: timed out after {FetchTimeout.TotalSeconds:0} seconds."));
            return (int)CliExitCode.TotalFailure;
        }

        foreach (var failed in results.Where(static result => result.Error is not null))
        {
            Console.Error.WriteLine($"Error ({failed.Key.Value}): {failed.Error!.Message}");
        }

        if (invocation.Format == CliOutputFormat.Json)
        {
            var payload = CliPayloadBuilder.Build(results);
            Console.Out.WriteLine(CliPayloadBuilder.ToJson(payload, invocation.Pretty));
        }
        else
        {
            var text = CliTextRenderer.Render(results, DateTimeOffset.Now);
            if (text.Length > 0)
            {
                Console.Out.WriteLine(text);
            }
        }

        return (int)CliExitCodes.FromResults(results, invocation.RequireAll);
    }

    private static string Version()
    {
        var informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+')[0];
        }

        return typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
