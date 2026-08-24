using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleanuparr.Persistence.Migrations.Data
{
    /// <inheritdoc />
    public partial class AddAiImportConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ai_import_breaker_cooldown_minutes",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_breaker_failure_threshold",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_confidence_threshold",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_decision_cache_ttl_hours",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ai_import_enabled",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ai_import_model",
                table: "queue_cleaner_configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ai_import_ollama_url",
                table: "queue_cleaner_configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ai_import_skip_budget",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ai_import_target_message_prefix",
                table: "queue_cleaner_configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ai_import_tick_budget_seconds",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ai_import_timeout_seconds",
                table: "queue_cleaner_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_import_breaker_cooldown_minutes",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_breaker_failure_threshold",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_confidence_threshold",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_decision_cache_ttl_hours",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_enabled",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_model",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_ollama_url",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_skip_budget",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_target_message_prefix",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_tick_budget_seconds",
                table: "queue_cleaner_configs");

            migrationBuilder.DropColumn(
                name: "ai_import_timeout_seconds",
                table: "queue_cleaner_configs");
        }
    }
}
