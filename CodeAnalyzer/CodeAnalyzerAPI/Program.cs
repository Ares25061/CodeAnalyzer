using CodeAnalyzerAPI.Services;
using Ollama;
using System.Reflection;

namespace CodeAnalyzerAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddSwaggerGen(c =>
            {
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }

                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "Code Analyzer API",
                    Version = "v1",
                    Description = "API для анализа структуры кода и проверки критериев качества"
                });
            });

            builder.Services.AddScoped<IProjectStructureAnalyzer, ProjectStructureAnalyzer>();
            builder.Services.AddScoped<ICriteriaValidator, CriteriaValidator>();
            builder.Services.AddScoped<ICustomCriteriaService, CustomCriteriaService>();
            builder.Services.AddScoped<IWordDocumentAnalyzer, WordDocumentAnalyzer>();

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
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Code Analyzer API v1");
            });

            app.UseHttpsRedirection();
            app.UseCors("AllowSpecificOrigins");
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}