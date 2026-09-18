namespace FixPal.Features.Dalil;

public static class DalilServiceCollectionExtensions
{
    public static IServiceCollection AddDalilAssistant(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DalilAssistantOptions>()
            .Bind(configuration.GetSection(DalilAssistantOptions.SectionName));
        services.AddHttpClient<IDalilAssistantService, GeminiDalilAssistantService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
