using CodeAnalyzerAPI.Services;
using Ollama;

namespace CodeAnalyzerAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            // Register services
            builder.Services.AddScoped<IProjectStructureAnalyzer, ProjectStructureAnalyzer>();
            builder.Services.AddScoped<ICriteriaValidator, CriteriaValidator>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigins", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyHeader()
                           .AllowAnyMethod();
                });
            });
            builder.Services.AddSingleton(s =>
            {
                var httpClient = new HttpClient
                {
                    BaseAddress = new Uri("https://domainollamaforproject-ru.tail8590fc.ts.net/api/")
                };
                return new OllamaApiClient(httpClient);
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.UseCors("AllowSpecificOrigins");
            app.UseHttpsRedirection();
            app.MapControllers();
            app.Run();

            app.Run();
        }
    }
}
