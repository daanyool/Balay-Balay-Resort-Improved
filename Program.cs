using Balay_Balay_Resort.Data;
using Balay_Balay_Resort.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSession();

// Allow large file uploads (100 MB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104_857_600; // 100 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
});

var app = builder.Build();

// Pre-create the property images upload directory
var uploadsDir = Path.Combine(builder.Environment.WebRootPath, "images", "properties");
if (!Directory.Exists(uploadsDir))
    Directory.CreateDirectory(uploadsDir);

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var adminEmail = "admin@rentgala.com";

    var adminExists = context.Users.Any(u => u.Email == adminEmail);

    if (!adminExists)
    {
        var adminUser = new User
        {
            FirstName = "System",
            LastName = "Admin",
            Email = adminEmail,
            PhoneNumber = "09478412351",
            Password = "admin123",
            ProfileImagePath = "/images/profile-picture.jpg",
            UserType = "Admin"
        };

        context.Users.Add(adminUser);
        context.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // serves dynamically uploaded files (images/properties/)
app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();


app.Run();
