using Voxa.Api;
using Voxa.Api.Http;
using Voxa.Api.Ingest;
using Voxa.Core.Remote;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.Section));
builder.Services.AddSingleton<ModelHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ModelHost>());
builder.Services.AddSingleton<PublicUrlGuard>(_ => new PublicUrlGuard());
builder.Services.AddSingleton<MediaResolver>();
builder.Services.AddSingleton<FfmpegAudioReader>();
builder.Services.AddSingleton<IngestService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = RemoteProtocol.Json.PropertyNamingPolicy);

// The bookmarklet runs inside any web page and the PWA on its own domain: every origin may call the API. No cookies
// are involved; the API key (ApiKeyFilter) is what protects it.
builder.Services.AddCors(o => o.AddPolicy(ApiEndpoints.CorsPolicy, policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
// Pings keep idle connections (nobody speaking) from being cut by proxies such as the Container Apps ingress.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.MapVoxaApi();
app.Run();
