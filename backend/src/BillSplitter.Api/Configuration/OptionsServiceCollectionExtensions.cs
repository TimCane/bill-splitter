namespace BillSplitter.Api.Configuration;

public static class OptionsServiceCollectionExtensions
{
    /// <summary>
    /// Binds every Options class from configuration (appsettings + env vars,
    /// <c>Section__Key</c> form). Required sections validate on start so a
    /// missing setting kills the host loudly rather than failing at first use.
    /// SMTP is optional and validates lazily.
    /// </summary>
    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AppOptions>()
            .Bind(config.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RedisOptions>()
            .Bind(config.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MinioOptions>()
            .Bind(config.GetSection(MinioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OcrOptions>()
            .Bind(config.GetSection(OcrOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SessionOptions>()
            .Bind(config.GetSection(SessionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(config.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations();

        return services;
    }
}
