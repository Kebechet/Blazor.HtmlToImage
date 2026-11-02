using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;

namespace Kebechet.Blazor.HtmlToImage.Tests;

/// <summary>
/// Guards the changes this package carries on top of the official html-to-image bundle. Upgrading the
/// bundle replaces the file wholesale, so a patch that is not re-applied disappears silently - the
/// capture still works everywhere except where the patch was needed.
/// </summary>
public class VendoredBundleTests
{
    /// <summary>
    /// Resolved from this file's compile-time path rather than the test assembly's location: an RCL's
    /// <c>wwwroot</c> is not copied into a referencing project's output, and these assertions are about
    /// repository content, not build output.
    /// </summary>
    private static string _bundlePath => Path.Combine(_repositoryRoot, "src", "Blazor.HtmlToImage", "wwwroot", "html-to-image.js");

    private static string _repositoryRoot
    {
        get
        {
            var testsDirectory = Path.GetDirectoryName(GetThisFilePath())!;
            return Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        }
    }

    /// <summary>
    /// The bundle carries the change from https://github.com/bubkoo/html-to-image/pull/602 until an
    /// html-to-image release ships it (https://github.com/bubkoo/html-to-image/issues/601).
    /// </summary>
    /// <remarks>
    /// Without it, an image served with its Content-Type header twice - which .NET MAUI's BlazorWebView
    /// does on Android for every static file - is embedded as <c>data:image/png;base64, image/png;base64</c>
    /// and the capture rejects with a bare <c>[object Event]</c>. Once the release that contains the fix
    /// is vendored, the test, the banner and the README section go. The snippets use the 1.11.13
    /// minified names: <c>x</c> is makeDataUrl, <c>r</c> is the FileReader in fetchAsDataURL and the
    /// content type in resourceToDataURL.
    /// </remarks>
    [Theory]
    [InlineData("function withSingleMediaType(t){var e=t.indexOf(\";base64,\");if(-1===e)return t;var n=t.slice(5,e);return-1===n.indexOf(\",\")?t:x(t.slice(e+8),firstMediaType(n))}")]
    [InlineData("result:withSingleMediaType(r.result)")]
    [InlineData("r=firstMediaType(e.headers.get(\"Content-Type\")||\"\")")]
    public void VendoredBundle_ResponseWithRepeatedContentType_KeepsOneMediaType(string patchedCode)
    {
        // Arrange
        var bundlePath = _bundlePath;

        // Act
        var bundle = File.ReadAllText(bundlePath);

        // Assert
        bundle.ShouldContain(patchedCode, Case.Sensitive);
    }

    private static string GetThisFilePath([CallerFilePath] string path = "")
    {
        return path;
    }
}
