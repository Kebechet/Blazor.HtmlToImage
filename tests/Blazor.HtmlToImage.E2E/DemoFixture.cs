using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Blazor.HtmlToImage.E2E;

[CollectionDefinition(Name)]
public sealed class DemoCollectionDefinition : ICollectionFixture<DemoFixture>
{
    public const string Name = "BlazingStory demo";
}

public sealed class DemoFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> _serverOutput = new();
    private Process? _demoProcess;
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = string.Empty;
    public IBrowser Browser { get; private set; } = null!;
    public IPage Page { get; private set; } = null!;

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
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "demo", "Blazor.HtmlToImage.Demo.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseUrl);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        _demoProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _demoProcess.OutputDataReceived += CaptureServerOutput;
        _demoProcess.ErrorDataReceived += CaptureServerOutput;
        if (!_demoProcess.Start())
        {
            throw new InvalidOperationException("Could not start the BlazingStory demo process.");
        }

        _demoProcess.BeginOutputReadLine();
        _demoProcess.BeginErrorReadLine();
        await WaitForDemoAsync(TimeSpan.FromMinutes(5));

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // The system Chrome rather than Playwright's bundled Chromium, so neither CI nor a
            // fresh clone needs a browser download step.
            Channel = "chrome",
            Headless = true,
            Args = ["--disable-dev-shm-usage"]
        });

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
    /// Navigates straight to a story canvas, then waits until the vendored library is actually on
    /// the page.
    /// </summary>
    /// <remarks>
    /// The canvas page (<c>iframe.html</c>) is what BlazingStory's shell hosts in an iframe anyway,
    /// so addressing it directly is faster and steadier - no sidebar, no panels, no cross-frame hop.
    /// <para>
    /// Waiting on <c>globalThis.htmlToImage</c> rather than on Blazor's first paint is the point:
    /// the package injects the script from its JS initializer, and that injection is exactly what
    /// these tests exist to prove. A capture fired before it resolves would fail for a reason that
    /// has nothing to do with the wrapper.
    /// </para>
    /// </remarks>
    public async Task<ILocator> NavigateToStoryAsync(string storyId)
    {
        lock (_jsErrors) _jsErrors.Clear();
        var url = $"{BaseUrl}/iframe.html?viewMode=story&id={storyId}&e2e={Guid.NewGuid():N}";
        await Page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Page.WaitForFunctionAsync(
            "() => typeof globalThis.htmlToImage !== 'undefined'",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        return Page.Locator("body");
    }

    /// <summary>Clicks a story's Capture button and waits for the panel to report a finished capture.</summary>
    public async Task<System.Text.Json.JsonElement> CaptureAsync(ILocator canvas, string testId)
    {
        await canvas.GetByTestId($"{testId}-capture").ClickAsync();

        return await StoryState.WaitForAsync(
            canvas,
            $"{testId}-state",
            state => state.TryGetProperty("hasCaptured", out var captured) && captured.GetBoolean(),
            $"the {testId} story to finish capturing");
    }

    /// <summary>
    /// Samples one pixel of a story's preview image, at fractional coordinates of its natural size.
    /// Returns [r, g, b, a].
    /// </summary>
    /// <remarks>
    /// This is what turns "the capture succeeded" into "the option reached the pixels": the preview
    /// is drawn onto a scratch canvas and read back, so the assertion is against the encoded image
    /// the library actually produced. Data-URL images are same-origin, so the canvas is not tainted.
    /// </remarks>
    public async Task<int[]> SamplePreviewPixelAsync(string testId, double xFraction, double yFraction)
    {
        return await Page.EvaluateAsync<int[]>(
            @"([id, xf, yf]) => new Promise((resolve, reject) => {
                const img = document.querySelector(`[data-testid=""${id}-preview""]`);
                if (!img) { reject(new Error('no preview image for ' + id)); return; }
                const sample = () => {
                    const canvas = document.createElement('canvas');
                    canvas.width = img.naturalWidth;
                    canvas.height = img.naturalHeight;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0);
                    const x = Math.min(canvas.width - 1, Math.round(canvas.width * xf));
                    const y = Math.min(canvas.height - 1, Math.round(canvas.height * yf));
                    resolve([...ctx.getImageData(x, y, 1, 1).data]);
                };
                img.complete && img.naturalWidth ? sample() : (img.onload = sample);
            })",
            new object[] { testId, xFraction, yFraction });
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

        if (_demoProcess is { HasExited: false })
        {
            _demoProcess.Kill(entireProcessTree: true);
            await _demoProcess.WaitForExitAsync();
        }

        _demoProcess?.Dispose();
    }

    private async Task WaitForDemoAsync(TimeSpan timeout)
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_demoProcess?.HasExited == true)
            {
                throw new InvalidOperationException($"The demo exited before becoming ready.{Environment.NewLine}{ServerLog()}");
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

        throw new TimeoutException($"The demo was not ready after {timeout}.{Environment.NewLine}{ServerLog()}");
    }

    private void CaptureServerOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
        {
            _serverOutput.Enqueue(args.Data);
        }
    }

    private string ServerLog()
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
