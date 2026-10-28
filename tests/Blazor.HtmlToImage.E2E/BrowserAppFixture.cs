using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// Runs one app project with <c>dotnet run</c> on a free port and opens it in a real browser.
/// </summary>
public abstract class BrowserAppFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> _serverOutput = new();
    private Process? _appProcess;
    private IPlaywright? _playwright;

    /// <summary>
    /// Unhandled JavaScript errors and Blazor render exceptions seen since the last navigation.
    /// </summary>
    /// <remarks>
    /// A capture can produce plausible bytes while the interop threw on the way - a failed font
    /// fetch, an image that never resolved, a filter applied to a text node that has no classList.
    /// The panel would still report a valid PNG and the test would pass. Asserting the console is
    /// what makes those visible, and none of it is reachable from bUnit, which stubs the interop
    /// module out entirely.
    /// </remarks>
    private readonly List<string> _jsErrors = new();

    public string BaseUrl { get; private set; } = string.Empty;
    public IBrowser Browser { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;

    /// <summary>The app's project file, relative to the repository root.</summary>
    protected abstract string ProjectPath { get; }

    public async ValueTask InitializeAsync()
    {
        var repositoryRoot = RepositoryRoot;
        var port = ReserveTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, ProjectPath));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseUrl);
        AddPackageUnderTest(startInfo);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        _appProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _appProcess.OutputDataReceived += CaptureServerOutput;
        _appProcess.ErrorDataReceived += CaptureServerOutput;
        if (!_appProcess.Start())
        {
            throw new InvalidOperationException($"Could not start {ProjectPath}.");
        }

        _appProcess.BeginOutputReadLine();
        _appProcess.BeginErrorReadLine();
        await WaitForAppAsync(TimeSpan.FromMinutes(5));

        _playwright = await Playwright.CreateAsync();
        Browser = await LaunchBrowserAsync(_playwright);

        Page = await Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 1000 }
        });

        Page.Console += (_, message) =>
        {
            if (message.Type == "error" && !IsUnrelatedNoise(message.Text))
            {
                lock (_jsErrors) _jsErrors.Add(FirstLine(message.Text));
            }
        };
        Page.PageError += (_, error) =>
        {
            lock (_jsErrors) _jsErrors.Add(FirstLine(error));
        };
    }

    /// <summary>
    /// Chrome by default; WebKit when <c>E2E_BROWSER</c> is <c>webkit</c>. WebKit is the engine behind
    /// every iOS browser and every MAUI app on iOS, and the one the stabilisation loop and capture
    /// timeout exist for.
    /// </summary>
    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        var browserName = Environment.GetEnvironmentVariable("E2E_BROWSER");
        if (string.IsNullOrEmpty(browserName) || browserName == "chrome")
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                // The system Chrome rather than Playwright's bundled Chromium, so neither CI nor a
                // fresh clone needs a browser download step.
                Channel = "chrome",
                Headless = true,
                Args = ["--disable-dev-shm-usage"]
            });
        }

        if (browserName == "webkit")
        {
            // Playwright's own WebKit build - install it once with `playwright.ps1 install webkit`
            // from the test project's build output.
            return await playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }

        throw new InvalidOperationException($"E2E_BROWSER must be chrome or webkit, not \"{browserName}\".");
    }

    /// <summary>
    /// Points the app at a packed .nupkg instead of the library project when
    /// <c>HTMLTOIMAGE_PACKAGE_VERSION</c> and <c>HTMLTOIMAGE_PACKAGE_SOURCE</c> (the folder holding
    /// it) are set - see <c>build/HtmlToImageReference.props</c>.
    /// </summary>
    /// <remarks>
    /// Restore and build output go under the package folder, away from the usual ones. A package
    /// version already in the global packages folder would otherwise be used instead of the new
    /// .nupkg with the same version, and the project-reference build output would be reused as-is.
    /// </remarks>
    private void AddPackageUnderTest(ProcessStartInfo startInfo)
    {
        var packageVersion = Environment.GetEnvironmentVariable("HTMLTOIMAGE_PACKAGE_VERSION");
        if (string.IsNullOrEmpty(packageVersion))
        {
            return;
        }

        var packageSource = Environment.GetEnvironmentVariable("HTMLTOIMAGE_PACKAGE_SOURCE");
        if (string.IsNullOrEmpty(packageSource))
        {
            throw new InvalidOperationException("HTMLTOIMAGE_PACKAGE_VERSION is set but HTMLTOIMAGE_PACKAGE_SOURCE, the folder holding the .nupkg, is not.");
        }

        var packageSourcePath = Path.GetFullPath(packageSource);
        startInfo.ArgumentList.Add($"-p:HtmlToImagePackageVersion={packageVersion}");
        startInfo.ArgumentList.Add($"-p:RestoreAdditionalProjectSources={packageSourcePath}");
        startInfo.ArgumentList.Add($"-p:RestorePackagesPath={Path.Combine(packageSourcePath, ".packages")}");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(Path.Combine(packageSourcePath, ".artifacts", Path.GetFileNameWithoutExtension(ProjectPath)));
    }

    /// <summary>Forgets the errors of earlier pages; call on every navigation.</summary>
    protected void ClearJsErrors()
    {
        lock (_jsErrors) _jsErrors.Clear();
    }

    /// <summary>Fails the current test if the page logged an unhandled JavaScript or Blazor render error.</summary>
    public void AssertNoJsErrors()
    {
        string[] errors;
        lock (_jsErrors) errors = _jsErrors.Distinct().ToArray();
        Assert.True(
            errors.Length == 0,
            "The page reported unhandled errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private static string FirstLine(string text)
    {
        var lines = text.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? text : lines[0];
    }

    /// <summary>Browser-extension chatter and asset noise that says nothing about this library.</summary>
    private static bool IsUnrelatedNoise(string text)
    {
        return text.Contains("contentscript")
            || text.Contains("Failed to load resource")
            || text.Contains("preloaded using link preload");
    }

    public async ValueTask DisposeAsync()
    {
        if (Page is not null)
        {
            await Page.CloseAsync();
        }

        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            await _appProcess.WaitForExitAsync();
        }

        _appProcess?.Dispose();
    }

    private async Task WaitForAppAsync(TimeSpan timeout)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_appProcess?.HasExited == true)
            {
                throw new InvalidOperationException($"{ProjectPath} exited before becoming ready.{Environment.NewLine}{ServerLog()}");
            }

            try
            {
                using var response = await client.GetAsync(BaseUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{ProjectPath} was not ready after {timeout}.{Environment.NewLine}{ServerLog()}");
    }

    private void CaptureServerOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
        {
            _serverOutput.Enqueue(args.Data);
        }
    }

    /// <summary>The last lines the app wrote to its console - where a server-side exception shows up.</summary>
    public string ServerLog()
    {
        return string.Join(Environment.NewLine, _serverOutput.TakeLast(100));
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>The repository root, found by walking up from the test assembly.</summary>
    public static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "demo", "Blazor.HtmlToImage.Demo.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Blazor.HtmlToImage.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Blazor.HtmlToImage repository root.");
    }
}
