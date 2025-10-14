using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
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

// ==== Новите по плана (интерфейси/репота/сервиси, които добавихме) ====
// (в зависимост от твоите namespace-и може да са вече покрити от горните using-и)

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
                    "Set it in Azure → App Service → Environment variables → Connection strings или локално в User Secrets.");

            if (string.IsNullOrWhiteSpace(blobConnStr))
                throw new InvalidOperationException(
                    "Missing connection string 'AzureBlob'. " +
                    "Set it в Azure → Environment variables → Connection strings или локално в User Secrets.");

            // ========= Infra: SQL (Dapper) =========
            // Някои от нашите репота ползват IConfiguration + new SqlConnection(...)
            // Оставяме и IDbConnection, ако някъде го инжектираш директно.
            builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(sqlConnStr));

            // ========= Infra: Blob =========
            // Ако преминеш на Managed Identity, тук ще ползваш конструктора с Uri + DefaultAzureCredential().
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

            // Seasons + Billing Months
            builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
            builder.Services.AddScoped<SeasonService>();

            // Club Fee Plans
            builder.Services.AddScoped<IClubFeePlanRepository, ClubFeePlanRepository>();
            builder.Services.AddScoped<FeePlanService>();

            // Training Groups
            builder.Services.AddScoped<ITrainingGroupRepository, TrainingGroupRepository>();
            builder.Services.AddScoped<TrainingGroupService>();

            // Schedule Slots
            builder.Services.AddScoped<IScheduleSlotRepository, ScheduleSlotRepository>();
            builder.Services.AddScoped<ScheduleSlotService>();

            // Athlete Slot Selections
            builder.Services.AddScoped<IAthleteSelectionRepository, AthleteSelectionRepository>();
            builder.Services.AddScoped<AthleteSelectionService>();

            // Payments
            builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
            builder.Services.AddScoped<PaymentService>();

            // ========= CORS =========
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader());
            });

            var app = builder.Build();

            // ========= Pipeline =========
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors("AllowAll");

            // Ако НЯМА конфигуриран authentication handler, коментирай следния ред.
            // app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
