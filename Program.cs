using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SpoerStats2.ClassRepository;
using SpoerStats2.IRepository;
using SpoerStats2.Repository;
using SpoerStats2.Service;
using SpoerStats2.Services;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;

namespace SpoerStats2
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // =========================
            // ✅ FORCED LOGGING -> Console + VS Output (надвива appsettings)
            // =========================
            builder.Host.ConfigureLogging((ctx, logging) =>
            {
                logging.ClearProviders();

                logging.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                });

                logging.AddDebug();

                logging.SetMinimumLevel(LogLevel.Information);

                // по-малко спам от framework-а
                logging.AddFilter("Microsoft", LogLevel.Warning);
                logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
                logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

                // твоите логове
                logging.AddFilter("SpoerStats2", LogLevel.Information);
            });

            // =========================
            // Controllers
            // =========================
            builder.Services.AddControllers()
                .AddJsonOptions(o =>
                {
                    o.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            builder.Services.AddEndpointsApiExplorer();

            // =========================
            // Swagger
            // =========================
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SportStats API", Version = "v1" });

                c.OperationFilter<FileUploadOperationFilter>();

                if (builder.Environment.IsDevelopment())
                    c.OperationFilter<SwaggerDefaultHeadersFilter>();

                c.MapType<IFormFile>(() => new OpenApiSchema
                {
                    Type = "string",
                    Format = "binary"
                });
            });

            // =========================
            // ✅ HTTP Logging (да виждаш заявки/статус кодове)
            // =========================
            builder.Services.AddHttpLogging(o =>
            {
                o.LoggingFields =
                    HttpLoggingFields.RequestMethod |
                    HttpLoggingFields.RequestPath |
                    HttpLoggingFields.RequestQuery |
                    HttpLoggingFields.RequestHeaders |
                    HttpLoggingFields.ResponseStatusCode |
                    HttpLoggingFields.Duration;

                o.RequestHeaders.Add("X-Admin-Key");
                o.RequestBodyLogLimit = 4096;
                o.ResponseBodyLogLimit = 4096;
            });

            // =========================
            // Config / Connection strings
            // =========================
            var sqlConnStr = builder.Configuration.GetConnectionString("DefaultConnection");
            var blobConnStr = builder.Configuration.GetConnectionString("AzureBlob");

            if (string.IsNullOrWhiteSpace(sqlConnStr))
                throw new InvalidOperationException(
                    "Missing connection string 'DefaultConnection'. " +
                    "Set it в Azure → App Service → Configuration → Connection strings или локално в User Secrets.");

            if (string.IsNullOrWhiteSpace(blobConnStr))
                throw new InvalidOperationException(
                    "Missing connection string 'AzureBlob'. " +
                    "Set it в Azure → Configuration → Connection strings или локално в User Secrets.");

            builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(sqlConnStr));

            builder.Services.AddSingleton(_ => new BlobServiceClient(blobConnStr));
            builder.Services.AddScoped<BlobStorageService>();

            builder.Services.Configure<FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = 60L * 1024 * 1024; // 60 MB
            });

            builder.Services.AddHttpClient<ISwimrankingsHttp, SwimrankingsHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = new CookieContainer(),
                    AutomaticDecompression = DecompressionMethods.All,
                    AllowAutoRedirect = true
                });

            // ========= Репозитории =========
            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<IClubRepository, ClubRepository>();
            builder.Services.AddScoped<IDisciplineRepository, DisciplineRepository>();
            builder.Services.AddScoped<IResultRepository, ResultRepository>();
            builder.Services.AddScoped<IRankingRepository, RankingRepository>();
            builder.Services.AddScoped<INormativeRepository, NormativeRepository>();
            builder.Services.AddScoped<IRankRepository, RankRepository>();
            builder.Services.AddScoped<IRoleRepository, RoleRepository>();
            builder.Services.AddScoped<IClubDisciplineRepository, ClubDisciplineRepository>();
            builder.Services.AddScoped<ISportRepository, SportRepository>();
            builder.Services.AddScoped<IMessageRepository, MessageRepository>();

            // ========= Сервизи =========
            builder.Services.AddScoped<UserService>();
            builder.Services.AddScoped<ClubService>();
            builder.Services.AddScoped<DisciplineService>();
            builder.Services.AddScoped<ResultService>();
            builder.Services.AddScoped<RankingService>();
            builder.Services.AddScoped<NormativeService>();
            builder.Services.AddScoped<RankService>();
            builder.Services.AddScoped<RoleService>();
            builder.Services.AddScoped<SportService>();
            builder.Services.AddScoped<ClubDisciplineService>();
            builder.Services.AddScoped<MessageService>();

            builder.Services.AddMemoryCache();

            builder.Services.AddScoped<SwimrankingsService>();

            // ========= Нови по плана =========
            builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
            builder.Services.AddScoped<SeasonService>();

            builder.Services.AddScoped<IClubFeePlanRepository, ClubFeePlanRepository>();
            builder.Services.AddScoped<FeePlanService>();

            builder.Services.AddScoped<ITrainingGroupRepository, TrainingGroupRepository>();
            builder.Services.AddScoped<TrainingGroupService>();

            builder.Services.AddScoped<IScheduleSlotRepository, ScheduleSlotRepository>();
            builder.Services.AddScoped<ScheduleSlotService>();

            builder.Services.AddScoped<LenexImportService>();

            builder.Services.AddScoped<IAthleteSelectionRepository, AthleteSelectionRepository>();
            builder.Services.AddScoped<AthleteSelectionService>();

            builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
            builder.Services.AddScoped<PaymentService>();

            builder.Services.AddScoped<IChatRepository, ChatRepository>();
            builder.Services.AddScoped<ChatService>();

            // ========= ✅ SESSION =========
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(o =>
            {
                o.IdleTimeout = TimeSpan.FromHours(12);
                o.Cookie.HttpOnly = true;
                o.Cookie.IsEssential = true;

                // ✅ важно за cross-site (frontend localhost/127.0.0.1 -> API localhost)
                o.Cookie.SameSite = SameSiteMode.None;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

            builder.Services.Configure<SpoerStats2.Models.AdminImportOptions>(
                builder.Configuration.GetSection("AdminImport"));

            builder.Services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SpoerStats2.Models.AdminImportOptions>>().Value;
                return new SpoerStats2.Service.AdminKeyValidator(opts.Key);
            });

            // ========= ✅ CORS =========
            // Чете от config, но ДОБАВЯ и локалните dev origins (не ги заменя)
            var configOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                                 (builder.Configuration["Cors:AllowedOrigins"] ?? "")
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                .Where(s => !string.IsNullOrWhiteSpace(s));

            var localDevOrigins = new[]
            {
                // Vite / React / други
                "http://localhost:5173",
                "https://localhost:5173",
                "http://localhost:3000",
                "https://localhost:3000",

                // Live Server (VS Code) — ✅ твоят случай
                "http://127.0.0.1:5501",
                "http://localhost:5501",
                "https://127.0.0.1:5501",
                "https://localhost:5501",

                // same-origin тестове към API host
                "http://localhost:7198",
                "https://localhost:7198"
            };

            var allowedOrigins = configOrigins
                .Concat(localDevOrigins)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials(); // ✅ required because fetch(..., { credentials: 'include' })
                });
            });

            var app = builder.Build();

            // ✅ BOOT маркер (ако това не го видиш, не стартираш този код)
            app.Logger.LogInformation("=== BOOT SpoerStats2 OK | Env={Env} ===", app.Environment.EnvironmentName);
            app.Logger.LogInformation("CORS allowed origins: {Origins}", string.Join(", ", allowedOrigins));

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            // ✅ HTTP request/response логове
            app.UseHttpLogging();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SportStats API v1");
            });

            app.UseHttpsRedirection();

            // не е задължително при minimal hosting, но е чисто и ясно
            app.UseRouting();

            // ✅ CORS преди Session / Authorization / MapControllers
            app.UseCors("Frontend");

            app.UseSession();

            app.UseAuthorization();
            app.UseStaticFiles();
            app.MapControllers();

            app.Run();
        }
    }

    // ✅ прави "Choose file" вместо ContentType/Headers/Length...
    public class FileUploadOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var fileParams = context.ApiDescription.ParameterDescriptions
                .Where(p => p.Type == typeof(IFormFile) ||
                            p.Type == typeof(IFormFileCollection) ||
                            (p.Type.IsGenericType &&
                             p.Type.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                             p.Type.GetGenericArguments()[0] == typeof(IFormFile)))
                .ToList();

            if (fileParams.Count == 0) return;

            var schema = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>(),
                Required = new HashSet<string>()
            };

            foreach (var p in fileParams)
            {
                var name = p.Name;

                if (p.Type == typeof(IFormFile))
                {
                    schema.Properties[name] = new OpenApiSchema { Type = "string", Format = "binary" };
                    schema.Required.Add(name);
                    continue;
                }

                schema.Properties[name] = new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Type = "string", Format = "binary" }
                };
                schema.Required.Add(name);
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content =
                {
                    ["multipart/form-data"] = new OpenApiMediaType { Schema = schema }
                }
            };
        }
    }

    // ✅ DEV only: auto-fill header-ите в Swagger (да не пишеш нищо)
    public class SwaggerDefaultHeadersFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation.Parameters == null) return;

            foreach (var p in operation.Parameters)
            {
                if (p.In != ParameterLocation.Header) continue;

                if (p.Name.Equals("Requester-Id", StringComparison.OrdinalIgnoreCase))
                {
                    p.Required = false;
                    p.Schema.Default = new OpenApiInteger(167);
                }
                else if (p.Name.Equals("Role-Id", StringComparison.OrdinalIgnoreCase))
                {
                    p.Required = false;
                    p.Schema.Default = new OpenApiInteger(2);
                }
                else if (p.Name.Equals("Club-Id", StringComparison.OrdinalIgnoreCase))
                {
                    p.Required = false;
                    p.Schema.Default = new OpenApiInteger(1);
                }
            }
        }
    }
}