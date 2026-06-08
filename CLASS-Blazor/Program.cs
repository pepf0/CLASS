using CLASS_Blazor.Components;
using CLASS_Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
});
builder.Services.AddScoped<DashboardDataService>();
builder.Services.AddScoped<ProfileImageStorageService>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddHttpClient<TutorDirectoryService>(client =>
{
    client.BaseAddress = new Uri("https://pepf.net/api/class/");
});
builder.Services.AddHttpClient<UserProfileService>(client =>
{
    client.BaseAddress = new Uri("https://pepf.net/api/class/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
