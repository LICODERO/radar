var builder = WebApplication.CreateBuilder(args);

// Local-only server: never listen on anything but the loopback interface.
builder.WebHost.UseUrls(builder.Configuration["Radar:Url"] ?? "http://127.0.0.1:5178");

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "R.A.D.A.R" }));

// The Angular build is served as static files (copied to wwwroot by the build script).
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
