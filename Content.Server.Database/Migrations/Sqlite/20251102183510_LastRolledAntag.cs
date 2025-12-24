// SPDX-FileCopyrightText: 2022 DrSmugleaf
// SPDX-FileCopyrightText: 2024 AJCM-git
// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class LastRolledAntag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "last_rolled_antag",
                table: "player",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_rolled_antag",
                table: "player");
        }
    }
}
