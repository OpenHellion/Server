﻿// UnregisterServer.cs
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

using System;
using Newtonsoft.Json;

namespace OpenHellion.Net.Message.MainServer;

[Serializable]
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class UnregisterServer : NakamaMessage
{
	public string ServerId;

	public int GamePort;

	public int StatusPort;

	public override string GetDestination()
	{
		return "unregister_server";
	}
}
