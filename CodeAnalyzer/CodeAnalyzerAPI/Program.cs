using CodeAnalyzerAPI.Services;
using Ollama;

namespace CodeAnalyzerAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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