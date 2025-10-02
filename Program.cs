using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient; // препоръчителният клиент
using System.Data;

// Repo/Service слоеве
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
            builder.Services.AddControllers();
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

            // Ако нямаш конфигуриран authentication handler, махни следващия ред.
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
