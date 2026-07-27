using User.Api.Endpoints;
using Swashbuckle.AspNetCore.SwaggerUI;
using Microsoft.EntityFrameworkCore;
using User.Application.Abstractions;
using User.Application.Services;
using User.Infrastructure;
using User.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// 组合根：在唯一能引用所有层的地方完成装配
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddInfrastructure(builder.Configuration);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// 开发态 CORS：允许 Angular dev server（ng serve，默认 :4200）及旧 nginx 开发端口 :8082
// 跨域调用本服务。演示/开发用途；生产应改为具体前端域名或走网关同源。
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.WithOrigins("http://localhost:4200", "http://localhost:8082")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 确保数据库与模型一致（开发/演示用；生产环境应改用 EF Migration）。
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
// 容器只监听 HTTP(8080)，未配置 HTTPS 终结点与证书，因此关闭 HttpsRedirection，
// 否则所有 HTTP 请求（含 /swagger）会被重定向到不可达的 https 端口而打不开。
// app.UseHttpsRedirection();

// Swagger / OpenAPI 交互界面：开发演示场景始终开启（不受环境限制）。
// 若生产环境不想暴露，可改回用 if (app.Environment.IsDevelopment()) 包裹下面两行。
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi/v1.json", "User API v1");
});

// 开发态允许跨域（必须在 MapUserEndpoints 之前）
app.UseCors("DevCors");

// 用户微服务端点
app.MapUserEndpoints();

// 健康检查（供容器探针 / 网关使用）
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();

// 暴露 Program 类，便于集成测试使用 WebApplicationFactory<Program>
public partial class Program;
