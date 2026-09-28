using Microsoft.EntityFrameworkCore;

namespace CustomFormService.Api.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options);
