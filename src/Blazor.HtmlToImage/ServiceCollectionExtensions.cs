using Microsoft.Extensions.DependencyInjection;

namespace Kebechet.Blazor.HtmlToImage;

/// <summary>Registration helpers for the html-to-image wrapper.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IHtmlToImageService"/>. Scoped rather than singleton because the
    /// service caches an <c>IJSObjectReference</c>, which belongs to one circuit - a singleton
    /// would hand a torn-down circuit's module to the next user on Blazor Server.
    /// </summary>
    public static IServiceCollection AddHtmlToImage(this IServiceCollection services)
    {
        services.AddScoped<IHtmlToImageService, HtmlToImageService>();
        return services;
    }
}
