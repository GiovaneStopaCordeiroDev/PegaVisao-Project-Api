using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PegaVisaoApi.Data;
using PegaVisaoApi.Services;
using System.Text;
using PegaVisaoApi.Services.MelhorEnvio;
using PegaVisaoApi.Services.Frete;

var builder = WebApplication.CreateBuilder(args);
// Suspensão temporária aprovada pela loja. Variáveis do Render têm prioridade.
builder.Configuration.AddJsonFile("frete-teste.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);

// Add services to the container.

builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Digite: Bearer {seu token JWT}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// AutoMapper
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("https://pegavisao.vercel.app", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Banco de dados
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<PegaVisaoContext>(options =>
    options.UseNpgsql(connectionString));

// JWT
var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrEmpty(jwtKey))
{
    throw new InvalidOperationException(
        "A chave JWT não foi configurada."
    );
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents {
            OnTokenValidated = async context => {
                var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var versao = context.Principal?.FindFirst("versao")?.Value ?? "0";
                if (!int.TryParse(id, out var usuarioId) || !int.TryParse(versao, out var versaoToken)) { context.Fail("Sessão inválida."); return; }
                var db = context.HttpContext.RequestServices.GetRequiredService<PegaVisaoContext>();
                var atual = await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => (int?)u.VersaoSessao).SingleOrDefaultAsync();
                if (atual == null || atual != versaoToken) context.Fail("Sessão expirada. Entre novamente.");
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ValidateIssuer = false,
            ValidateAudience = false,

            ValidateLifetime = true,

            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient<IEmailRecuperacao, EmailRecuperacao>(client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IGoogleIdentidade, GoogleIdentidade>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("autenticacao", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions {
            PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});

builder.Services.AddHttpClient<MercadoPagoService>();

builder.Services.Configure<MelhorEnvioOptions>(builder.Configuration.GetSection("MelhorEnvio"));
builder.Services.AddSingleton<MelhorEnvioProtecao>();
builder.Services.AddHttpClient<MelhorEnvioOAuthClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<MelhorEnvioService>();
builder.Services.AddHostedService<MelhorEnvioRenovacaoWorker>();
builder.Services.AddHttpClient<MelhorEnvioFreteClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<FreteService>();
builder.Services.AddScoped<EstoqueService>();
builder.Services.AddHostedService<EstoqueConciliacaoWorker>();
var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

// IMPORTANTE: Authentication vem antes de Authorization
app.UseRateLimiter();
app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

