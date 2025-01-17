﻿using Maple2Storage.Types;
using MaplePacketLib2.Tools;
using MapleServer2.Constants;
using MapleServer2.Enums;
using MapleServer2.Managers;
using MapleServer2.Packets;
using MapleServer2.Servers.Game;

namespace MapleServer2.PacketHandlers.Game;

public class FallDamageHandler : GamePacketHandler<FallDamageHandler>
{
    public override RecvOp OpCode => RecvOp.StateFallDamage;

    public override void Handle(GameSession session, PacketReader packet)
    {
        float distance = packet.ReadFloat();
        if (distance > Block.BLOCK_SIZE * 6)
        {
            if (session.Player.Mount != null && session.Player.Levels.PrestigeLevel < (int) PrestigePerk.SafeRiding)
            {
                session.FieldManager.BroadcastPacket(MountPacket.StopRide(session.Player.FieldPlayer));
            }

            session.Player.FallDamage();
            TrophyManager.OnFallDamage(session.Player);
        }

        if (session.Player.OnAirMount)
        {
            session.Player.OnAirMount = false;
        }

        TrophyManager.OnFall(session.Player, distance);
    }
}
