using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ByHofman.EfCore.Provisioning.Tests.Postgres;

/// <summary>A context whose only content is its migrations: enough schema work that replicas racing over it collide.</summary>
internal sealed class RaceDbContext(DbContextOptions<RaceDbContext> options) : DbContext(options)
{
    public static DbContextOptions<RaceDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<RaceDbContext>()
            .UseNpgsql(connectionString)
            // The migrations are hand-written, without a model snapshot.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
}

[DbContext(typeof(RaceDbContext))]
[Migration("20261112000001_Notebooks")]
internal sealed class Notebooks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notebooks",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                title = table.Column<string>(maxLength: 200, nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_notebooks", x => x.id));
    }
}

[DbContext(typeof(RaceDbContext))]
[Migration("20261112000002_Pages")]
internal sealed class Pages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pages",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                notebook_id = table.Column<Guid>(nullable: false),
                body = table.Column<string>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pages", x => x.id);
                table.ForeignKey("fk_pages_notebooks", x => x.notebook_id, "notebooks", "id");
            });
        migrationBuilder.CreateIndex("ix_pages_notebook_id", "pages", "notebook_id");
    }
}

[DbContext(typeof(RaceDbContext))]
[Migration("20261112000003_PageTitles")]
internal sealed class PageTitles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("title", "pages", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("updated_at", "notebooks", nullable: true);
    }
}
