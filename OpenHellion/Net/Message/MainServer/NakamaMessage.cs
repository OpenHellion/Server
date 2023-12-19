// NakamaMessage.cs
//
// Copyright (C) 2023, OpenHellion contributors
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


using OpenHellion.IO;

namespace OpenHellion.Net.Message.MainServer;

public abstract class NakamaMessage
{
	public abstract string GetDestination();

	public override string ToString()
	{
		return JsonSerialiser.Serialize(this, JsonSerialiser.Formatting.None);
	}
}
