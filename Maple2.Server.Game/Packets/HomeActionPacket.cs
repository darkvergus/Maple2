﻿using System.Diagnostics;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class HomeActionPacket {
    private enum HomeActionCommand : byte {
        Alarm = 4,
        Survey = 5,
        PortalCube = 6,
        Ball = 7,
        Roll = 11,
    }

    private enum SurveyCommand : byte {
        Message = 0,
        Question = 2,
        AddOption = 3,
        Start = 4,
        Answer = 5,
        End = 6
    }

    private enum BallCommand : byte {
        Add = 0,
        Remove = 1,
        Update = 2,
        Hit = 3
    }

    public static ByteWriter SendCubePortalSettings(PlotCube cube, List<string> otherPortalsNames) {
        Debug.Assert(cube.CubePortalSettings != null, nameof(cube.CubePortalSettings) + " != null");

        var pWriter = Packet.Of(SendOp.HomeAction);
        pWriter.Write<HomeActionCommand>(HomeActionCommand.PortalCube);
        pWriter.WriteByte();
        pWriter.Write<Vector3B>(cube.Position);
        pWriter.WriteClass<CubePortalSettings>(cube.CubePortalSettings);
        pWriter.WriteInt(otherPortalsNames.Count);
        foreach (string portalName in otherPortalsNames) {
            pWriter.WriteUnicodeString(portalName);
        }

        return pWriter;
    }
}
