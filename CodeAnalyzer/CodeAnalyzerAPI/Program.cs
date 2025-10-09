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
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register services
            builder.Services.AddScoped<IProjectStructureAnalyzer, ProjectStructureAnalyzer>();
            builder.Services.AddScoped<ICriteriaValidator, CriteriaValidator>();

            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigins", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyHeader()
                           .AllowAnyMethod();
                });
            });

            // Ollama client - ПРАВИЛЬНАЯ настройка
            builder.Services.AddSingleton<OllamaApiClient>(provider =>
            {
                // Создаем HttpClient и передаем его в OllamaApiClient
                var httpClient = new HttpClient
                {
                    BaseAddress = new Uri("http://localhost:11434/")
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
            app.UseCors("AllowSpecificOrigins");
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}