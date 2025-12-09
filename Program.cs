using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient; // препоръчителният клиент
using System.Data;

// Repo/Service слоеве (съществуващи)
using SpoerStats2.ClassRepository;
using SpoerStats2.IRepository;
using SpoerStats2.Repository;
using SpoerStats2.Service;
using SpoerStats2.Services; // BlobStorageService

namespace SpoerStats2
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ========= Controllers / Swagger =========
            builder.Services.AddControllers()
                .AddJsonOptions(o =>
                {
                    // Пази PascalCase като в моделите (Dapper мапва по имена 1:1)
                    o.JsonSerializerOptions.PropertyNamingPolicy = null;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // ========= Connection strings =========
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

            // ========= Infra: SQL (Dapper) =========
            builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(sqlConnStr));

            // ========= Infra: Blob =========
            builder.Services.AddSingleton(_ => new BlobServiceClient(blobConnStr));
            builder.Services.AddScoped<BlobStorageService>();

            // ========= Multipart ограничения =========
            builder.Services.Configure<FormOptions>(o =>
            {
                o.MultipartBodyLengthLimit = 60L * 1024 * 1024; // 60 MB
            });

            // ========= Репозитории (съществуващи) =========
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

            // ========= Сервизи (съществуващи) =========
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

            // ========= Нови по плана: сезони/цени/групи/слотове/избори/плащания =========
            builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
            builder.Services.AddScoped<SeasonService>();

            builder.Services.AddScoped<IClubFeePlanRepository, ClubFeePlanRepository>();
            builder.Services.AddScoped<FeePlanService>();

            builder.Services.AddScoped<ITrainingGroupRepository, TrainingGroupRepository>();
            builder.Services.AddScoped<TrainingGroupService>();

            builder.Services.AddScoped<IScheduleSlotRepository, ScheduleSlotRepository>();
            builder.Services.AddScoped<ScheduleSlotService>();

            builder.Services.AddScoped<IAthleteSelectionRepository, AthleteSelectionRepository>();
            builder.Services.AddScoped<AthleteSelectionService>();

            builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
            builder.Services.AddScoped<PaymentService>();

            // ========= НОВО: Чатове =========
            builder.Services.AddScoped<IChatRepository, ChatRepository>(); // Dapper repo за чатове
            builder.Services.AddScoped<ChatService>();                     // Сервиз за чатове

            // ========= CORS =========
            // Чете позволените фронтенд адреси от Cors:AllowedOrigins (CSV).
            // Пример: "https://sportstatsbg.com,https://www.sportstatsbg.com,https://<storage>.z13.web.core.windows.net"
            var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                                  (builder.Configuration["Cors:AllowedOrigins"] ?? "")
                                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .ToArray();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Frontend", policy =>
                {
                    if (allowedOrigins.Length > 0)
                    {
                        policy.WithOrigins(allowedOrigins)
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                        // Ако ще ползваш cookies/credentials, добави: .AllowCredentials();
                    }
                    else
                    {
                        // DEV fallback – не оставяй така в Production.
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                });
            });

            var app = builder.Build();

            // ========= Pipeline =========
            // За да уважава X-Forwarded-Proto/For от Azure Front Door / App Service
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            // Swagger – полезен и в Production зад защитен URL/slot
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();

            // CORS преди AuthZ
            app.UseCors("Frontend");

            // Ако НЯМА конфигуриран authentication handler, дръж коментиран реда по-долу.
            // app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
