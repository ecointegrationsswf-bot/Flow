using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookContractTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WebhookContractEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ConversationImpact",
                table: "ActionDefinitions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Transparent");

            migrationBuilder.AddColumn<string>(
                name: "ExecutionMode",
                table: "ActionDefinitions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FireAndForget");

            migrationBuilder.AddColumn<string>(
                name: "ParamSource",
                table: "ActionDefinitions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SystemOnly");

            migrationBuilder.AddColumn<string>(
                name: "RequiredParams",
                table: "ActionDefinitions",
                type: "nvarchar(max)",
                nullable: true);

            // ── Seed data: clasificación taxonómica de las acciones existentes ──
            // VALIDATE_IDENTITY: inline, mixto, bloquea respuesta
            migrationBuilder.Sql(@"
                UPDATE ActionDefinitions SET
                    ExecutionMode = 'Inline',
                    ParamSource = 'Mixed',
                    ConversationImpact = 'BlocksResponse',
                    RequiredParams = '[
                        {""name"":""documentNumber"",""source"":""conversation"",""required"":true,""description"":""Número de documento"",""agentPrompt"":""¿Cuál es tu número de cédula?""},
                        {""name"":""birthDate"",""source"":""conversation"",""required"":true,""description"":""Fecha de nacimiento"",""agentPrompt"":""¿Cuál es tu fecha de nacimiento?""}
                    ]'
                WHERE Name = 'VALIDATE_IDENTITY';
            ");

            // SEND_EMAIL_RESUME, SEND_DOCUMENT, SEND_RESUME: fire-and-forget, sistema, transparente
            migrationBuilder.Sql(@"
                UPDATE ActionDefinitions SET
                    ExecutionMode = 'FireAndForget',
                    ParamSource = 'SystemOnly',
                    ConversationImpact = 'Transparent',
                    RequiredParams = NULL
                WHERE Name IN ('SEND_EMAIL_RESUME','SEND_DOCUMENT','SEND_RESUME');
            ");

            // SEND_PAYMENT_LINK, PREMIUM: inline, mixto, bloquea respuesta
            migrationBuilder.Sql(@"
                UPDATE ActionDefinitions SET
                    ExecutionMode = 'Inline',
                    ParamSource = 'Mixed',
                    ConversationImpact = 'BlocksResponse',
                    RequiredParams = '[
                        {""name"":""amount"",""source"":""conversation"",""required"":true,""description"":""Monto"",""agentPrompt"":""¿Cuál es el monto?""},
                        {""name"":""reference"",""source"":""conversation"",""required"":false,""description"":""Referencia"",""agentPrompt"":""¿Tienes un número de referencia?""}
                    ]'
                WHERE Name IN ('SEND_PAYMENT_LINK','PREMIUM');
            ");

            // TRANSFER_CHAT, CLOSE_CONVERSATION, ESCALATE_TO_HUMAN, SEND_MESSAGE: inline, sistema, transparente
            migrationBuilder.Sql(@"
                UPDATE ActionDefinitions SET
                    ExecutionMode = 'Inline',
                    ParamSource = 'SystemOnly',
                    ConversationImpact = 'Transparent',
                    RequiredParams = NULL
                WHERE Name IN ('TRANSFER_CHAT','CLOSE_CONVERSATION','ESCALATE_TO_HUMAN','SEND_MESSAGE');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebhookContractEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ConversationImpact",
                table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "ParamSource",
                table: "ActionDefinitions");

            migrationBuilder.DropColumn(
                name: "RequiredParams",
                table: "ActionDefinitions");
        }
    }
}
