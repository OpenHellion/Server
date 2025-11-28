// ConsoleReader.cs
//
// Copyright (C) 2025, OpenHellion contributors
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using OpenHellion.IO;
using ZeroGravity;
using ZeroGravity.Objects;

namespace OpenHellion;

class ConsoleReader
{
	public async Task ReadLoopAsync(Server server, CancellationToken cancellationToken)
	{
		Console.WriteLine("Type 'help' for available commands.");

		while (Server.IsRunning)
		{
			await Task.Delay(50, cancellationToken); // Avoid busy waiting

			var input = Console.ReadLine();

			if (input == null)
				continue;

			if (string.IsNullOrWhiteSpace(input))
				continue;

			string[] parts = input.ToLower().Split(" ", StringSplitOptions.RemoveEmptyEntries);
			string command = parts[0].Trim();

			switch (command)
			{
				case "help":
					Console.WriteLine("Available commands:");
					Console.WriteLine("  help - Show this help message");
					Console.WriteLine("  exit - Exit the console reader");
					Console.WriteLine("  info - Show server information");
					Console.WriteLine("  info ship <guid> - Show ship information");
					Console.WriteLine("  info player <guid> - Show player information");
					break;
				case "exit":
					Console.WriteLine("Exiting...");
					Server.IsRunning = false;
					break;
				case "info":
					if (parts.Length > 2)
					{
						string infoType = parts[1].Trim();
						switch (infoType)
						{
							case "player":
								if (long.TryParse(parts[2].Trim(), out long playerId))
								{
									Player player = server.GetPlayer(playerId);
									if (player != null && player.GetPersistenceData() != null)
									{
										JsonSerialiser.SerializeToFile(player.GetPersistenceData(), "player_" + playerId + ".json", JsonSerialiser.Formatting.Indented);
										Console.WriteLine($"Player data saved to player_{playerId}.json");
									}
									else
									{
										Console.WriteLine($"No player found with ID {playerId}");
									}
								}
								else
								{
									Console.WriteLine("Invalid player ID.");
								}
								break;
							case "ship":
								if (long.TryParse(parts[2].Trim(), out long shipId))
								{
									if (server.GetObject(shipId) is Ship ship)
									{
										JsonSerialiser.SerializeToFile(ship.GetPersistenceData(), "ship_" + shipId + ".json", JsonSerialiser.Formatting.Indented);
										Console.WriteLine($"Ship data saved to ship_{shipId}.json");
									}
									else
									{
										Console.WriteLine($"No ship found with ID {shipId}");
									}
								}
								else
								{
									Console.WriteLine("Invalid ship ID.");
								}
								break;
							default:
								Console.WriteLine($"Unknown info type: {infoType}");
								break;
						}
					}
					else
					{
						Console.WriteLine(server.AllPlayers.Count + " players connected.");
						Console.WriteLine(server.AllVessels.Count + " vessels in the game.");
						Console.WriteLine("See help for more information on the 'info' command.");
					}
					break;
				default:
					Console.WriteLine($"Unknown command: {command}");
					break;
			}
		}
	}
}
