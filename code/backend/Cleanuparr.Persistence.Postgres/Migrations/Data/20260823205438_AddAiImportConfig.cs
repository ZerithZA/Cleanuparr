using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleanuparr.Persistence.Postgres.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddAiImportConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ai_import_breaker_cooldown_minutes",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_breaker_failure_threshold",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_confidence_threshold",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_decision_cache_ttl_hours",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ai_import_enabled",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ai_import_model",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ai_import_ollama_url",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ai_import_skip_budget",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ai_import_target_message_prefix",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ai_import_tick_budget_seconds",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_timeout_seconds",
                schema: "data",
                table: "queue_cleaner_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_import_breaker_cooldown_minutes",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_breaker_failure_threshold",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_confidence_threshold",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_decision_cache_ttl_hours",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_enabled",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_model",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_ollama_url",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_skip_budget",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_target_message_prefix",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_tick_budget_seconds",
                schema: "data",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_timeout_seconds",
                schema: "data",
                table: "queue_cleaner_configs");
        }
    }
}
