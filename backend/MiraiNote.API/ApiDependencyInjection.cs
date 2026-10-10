using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MiraiNote.API.Services;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.AgentRuns;
using MiraiNote.Core.Services.ChatModels;
using MiraiNote.Shared.Common;

namespace MiraiNote.API;

public static class ApiDependencyInjection
{
    public const string CorsPolicyName = "MiraiNoteCors";

    /// <summary>
    /// 注册 API 层服务：HttpContextAccessor、CurrentUserService、JWT 认证、Options、CORS、Swagger。
    /// </summary>
    public static IServiceCollection AddApiLayer(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
    {
        // Options 绑定
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<DeepSeekOptions>(configuration.GetSection(DeepSeekOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.PostConfigure<DeepSeekOptions>(options =>
        {
            var provider = configuration.GetSection(AiOptions.SectionName)
                .Get<AiOptions>()?
                .Providers
                .FirstOrDefault(item => string.Equals(item.Key, "deepseek", StringComparison.OrdinalIgnoreCase));
            if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey)) return;

            // 旧服务仍使用 DeepSeekOptions；凭据统一由 AI Providers 提供。
            options.ApiKey = provider.ApiKey;
            if (!string.IsNullOrWhiteSpace(provider.BaseUrl)) options.BaseUrl = provider.BaseUrl;
        });
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.SectionName));
        services.Configure<TavilyOptions>(configuration.GetSection(TavilyOptions.SectionName));
        services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));
        services.Configure<QWeatherOptions>(configuration.GetSection(QWeatherOptions.SectionName));
        services.Configure<FileSystemOptions>(configuration.GetSection(FileSystemOptions.SectionName));
        services.Configure<AgentRunOptions>(configuration.GetSection(AgentRunOptions.SectionName));

        // 当前用户上下文
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // 内存缓存（登录失败计数、邮件频率限制）
        services.AddMemoryCache();

        // JWT Bearer 认证
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt 配置缺失");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();

        // CORS
        var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
            ?? new CorsOptions();
        var allowedOrigins = corsOptions.AllowedOrigins;
        if (allowedOrigins.Length == 0 && env.IsDevelopment())
        {
            allowedOrigins = new[] { "http://localhost:5173" };
        }
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    // 生产环境：如果前端由反向代理（Cloudflare 等）处理 CORS，请将 AllowedOrigins 置空，
                    // 避免 ASP.NET Core 和代理重复添加 CORS 响应头导致登录失败。
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                }
                // AllowedOrigins 为空时不添加任何 CORS 头，由上游代理统一处理。
            });
        });

        // Swagger（OpenAPI + Bearer 授权按钮）
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "MiraiNote API", Version = "v1" });

            c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "请输入 JWT，格式：Bearer {token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            });
        });

        return services;
    }
}
