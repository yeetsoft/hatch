using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropHomeAutomationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AirQualitySamples");

            migrationBuilder.DropTable(
                name: "CalendarEvents");

            migrationBuilder.DropTable(
                name: "CameraConnections");

            migrationBuilder.DropTable(
                name: "ControlOverrides");

            migrationBuilder.DropTable(
                name: "EnvironmentReadings");

            migrationBuilder.DropTable(
                name: "Measurements");

            migrationBuilder.DropTable(
                name: "PanelControlBindings");

            migrationBuilder.DropTable(
                name: "RoutineActions");

            migrationBuilder.DropTable(
                name: "StateChanges");

            migrationBuilder.DropTable(
                name: "WeatherAlerts");

            migrationBuilder.DropTable(
                name: "ZoneConfigs");

            migrationBuilder.DropTable(
                name: "Calendars");

            migrationBuilder.DropTable(
                name: "Commands");

            migrationBuilder.DropTable(
                name: "PanelItems");

            migrationBuilder.DropTable(
                name: "CalendarAccounts");

            migrationBuilder.DropTable(
                name: "ControlDecisions");

            migrationBuilder.DropTable(
                name: "DeviceChannels");

            migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropTable(
                name: "Routines");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Zones");

            // The settings that only the dropped tables' features ever read.
            // HomeAssistantToken and KioskWifiPassword are secrets at rest -
            // the same reason 20261111214802_DropFamilyAppSchemas.cs deleted
            // ImmichApiKey - and the rest are dead data nothing will read
            // again. Down() does not restore these rows: the pre-upgrade
            // pg_dump is the restore, as the Family migration's is.
            migrationBuilder.Sql(
                "DELETE FROM \"SiteSettings\" WHERE \"Key\" IN (" +
                "'Latitude', 'Longitude', 'WeatherEntity', 'ComfortToleranceF', " +
                "'DefaultComfortLowF', 'DefaultComfortHighF', 'OverrideBackoffMinutes', " +
                "'HomeAssistantHost', 'HomeAssistantPort', 'HomeAssistantToken', " +
                "'MediaLibraryBaseUrl', 'KioskWifiSsid', 'KioskWifiPassword', 'KioskWifiSecurityType', " +
                "'CalendarAgendaDays', 'WeatherAlertProvider', 'AirQualityProvider', " +
                "'WeatherAlertContact', 'AirQualityAlertThresholdAqi', 'HazardMaxSeverityAgeHours');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AirQualitySamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    No2 = table.Column<decimal>(type: "numeric", nullable: true),
                    Ozone = table.Column<decimal>(type: "numeric", nullable: true),
                    Pm10 = table.Column<decimal>(type: "numeric", nullable: true),
                    Pm25 = table.Column<decimal>(type: "numeric", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsAqi = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AirQualitySamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalendarAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccountEmail = table.Column<string>(type: "text", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncError = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NeedsReauth = table.Column<bool>(type: "boolean", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ControlDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChosenActionJson = table.Column<string>(type: "text", nullable: false),
                    InputsJson = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    RunnerUpJson = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<decimal>(type: "numeric", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DesiredTemperature = table.Column<decimal>(type: "numeric", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    Humidity = table.Column<decimal>(type: "numeric", nullable: true),
                    IsHeating = table.Column<bool>(type: "boolean", nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Panels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Routines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    IsToggle = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Routines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeatherAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    AreaDescription = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Ends = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Event = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Headline = table.Column<string>(type: "text", nullable: true),
                    Instruction = table.Column<string>(type: "text", nullable: true),
                    Onset = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderAlertId = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZoneConfigs",
                columns: table => new
                {
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    ComfortHighF = table.Column<decimal>(type: "numeric", nullable: true),
                    ComfortLowF = table.Column<decimal>(type: "numeric", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneConfigs", x => x.EntityId);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComfortHighF = table.Column<decimal>(type: "numeric", nullable: true),
                    ComfortLowF = table.Column<decimal>(type: "numeric", nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ColorOverride = table.Column<string>(type: "text", nullable: true),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProviderCalendarId = table.Column<string>(type: "text", nullable: false),
                    ProviderColor = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calendars_CalendarAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "CalendarAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PanelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Color = table.Column<string>(type: "text", nullable: true),
                    ControlKind = table.Column<int>(type: "integer", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    MaxF = table.Column<decimal>(type: "numeric", nullable: true),
                    MinF = table.Column<decimal>(type: "numeric", nullable: true),
                    OnMode = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    StepF = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelItems_Panels_PanelId",
                        column: x => x.PanelId,
                        principalTable: "Panels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PanelItems_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    HaDeviceId = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CalendarEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAllDay = table.Column<bool>(type: "boolean", nullable: false),
                    LocalEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LocalStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    ProviderEventId = table.Column<string>(type: "text", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarEvents_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CameraConnections",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveredHost = table.Column<string>(type: "text", nullable: true),
                    Host = table.Column<string>(type: "text", nullable: true),
                    PasswordProtected = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    StreamPath = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraConnections", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_CameraConnections_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableOptions = table.Column<string>(type: "text", nullable: true),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    HaAttribute = table.Column<string>(type: "text", nullable: true),
                    HaEntityId = table.Column<string>(type: "text", nullable: false),
                    Metric = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceChannels_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedValue = table.Column<string>(type: "text", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commands_ControlDecisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "ControlDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Commands_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Measurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Measurements_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelControlBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelControlBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelControlBindings_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PanelControlBindings_PanelItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "PanelItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoutineActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutineActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutineActions_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoutineActions_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StateChanges_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ControlOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedState = table.Column<string>(type: "text", nullable: true),
                    ObservedState = table.Column<string>(type: "text", nullable: true),
                    SuppressedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlOverrides_Commands_CommandId",
                        column: x => x.CommandId,
                        principalTable: "Commands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ControlOverrides_DeviceChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "DeviceChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ControlOverrides_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AirQualitySamples_Source_Timestamp",
                table: "AirQualitySamples",
                columns: new[] { "Source", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarAccounts_Provider_AccountEmail",
                table: "CalendarAccounts",
                columns: new[] { "Provider", "AccountEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_CalendarId_ProviderEventId",
                table: "CalendarEvents",
                columns: new[] { "CalendarId", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_AccountId_ProviderCalendarId",
                table: "Calendars",
                columns: new[] { "AccountId", "ProviderCalendarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commands_ChannelId_RequestedAt",
                table: "Commands",
                columns: new[] { "ChannelId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Commands_DecisionId",
                table: "Commands",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlDecisions_Timestamp",
                table: "ControlDecisions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ControlOverrides_ChannelId",
                table: "ControlOverrides",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlOverrides_CommandId",
                table: "ControlOverrides",
                column: "CommandId");

            migrationBuilder.CreateIndex(
                name: "IX_ControlOverrides_DeviceId_SuppressedUntil",
                table: "ControlOverrides",
                columns: new[] { "DeviceId", "SuppressedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChannels_DeviceId",
                table: "DeviceChannels",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ZoneId",
                table: "Devices",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentReadings_EntityId_Timestamp",
                table: "EnvironmentReadings",
                columns: new[] { "EntityId", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Measurements_ChannelId_Timestamp",
                table: "Measurements",
                columns: new[] { "ChannelId", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelControlBindings_ChannelId",
                table: "PanelControlBindings",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelControlBindings_ItemId",
                table: "PanelControlBindings",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelItems_PanelId",
                table: "PanelItems",
                column: "PanelId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelItems_RoutineId",
                table: "PanelItems",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineActions_ChannelId",
                table: "RoutineActions",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutineActions_RoutineId",
                table: "RoutineActions",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_StateChanges_ChannelId_Timestamp",
                table: "StateChanges",
                columns: new[] { "ChannelId", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeatherAlerts_Source_ProviderAlertId",
                table: "WeatherAlerts",
                columns: new[] { "Source", "ProviderAlertId" },
                unique: true);
        }
    }
}
