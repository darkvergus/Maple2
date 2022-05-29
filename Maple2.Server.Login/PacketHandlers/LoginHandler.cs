﻿using System;
using System.Collections.Generic;
using System.Net;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Core.Packets;
using Maple2.Server.Global.Service;
using Maple2.Server.Login.Session;
using Microsoft.Extensions.Logging;
using GlobalClient = Maple2.Server.Global.Service.Global.GlobalClient;

namespace Maple2.Server.Login.PacketHandlers;

public class LoginHandler : PacketHandler<LoginSession> {
    public override ushort OpCode => RecvOp.RESPONSE_LOGIN;

    private enum Command : byte {
        ServerList = 1,
        CharacterList = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public GlobalClient Global { private get; init; } = null!;
    // ReSharper restore All
    #endregion

    public LoginHandler(ILogger<LoginHandler> logger) : base(logger) { }

    public override void Handle(LoginSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        string user = packet.ReadUnicodeString();
        string pass = packet.ReadUnicodeString();
        packet.ReadShort(); // 1
        var machineId = packet.Read<Guid>();

        try {
            logger.LogDebug("Logging in with user:{User} pass:{Pass}", user, pass);
            LoginResponse response = Global.Login(new LoginRequest {
                Username = user,
                Password = pass,
                MachineId = machineId.ToString(),
            });
            if (response.Code != LoginResponse.Types.Code.Ok) {
                session.Send(LoginResultPacket.Error((byte) response.Code, response.Message, response.AccountId));
                session.Disconnect();
                return;
            }

            session.Init(response.AccountId, machineId);

            switch (command) {
                case Command.ServerList:
                    session.ListServers();
                    return;
                case Command.CharacterList:
                    session.Send(LoginResultPacket.Success(response.AccountId));
                    //session.Send(UgcPacket.SetEndpoint("http://127.0.0.1/ws.asmx?wsdl", "http://127.0.0.1"));

                    session.ListCharacters();
                    return;
                default:
                    logger.LogError("Invalid type: {Type}", command);
                    break;
            }
        } catch (RpcException ex) {
            logger.LogError(ex, "Failed to login");
        }

        // Disconnect by default if anything goes wrong.
        session.Disconnect();
    }
}