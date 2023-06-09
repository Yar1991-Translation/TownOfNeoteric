﻿using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using TOHE.Modules;
using TOHE.Roles.Core.Interfaces;
using TOHE.Roles.Core;

namespace TOHE.Roles.Crewmate;
public sealed class Medicaler : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        new(
            typeof(Medicaler),
            player => new Medicaler(player),
            CustomRoles.Medicaler,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Crewmate,
            8021866,
            SetupOptionItem,
            "me",
            "#00a4ff",
            true
        );
    public Medicaler(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        ProtectList = new();

        CustomRoleManager.MarkOthers.Add(MarkOthers);
        CustomRoleManager.OnCheckMurderPlayerOthers_Before.Add(OnCheckMurderPlayerOthers_Before);
    }

    static OptionItem OptionProtectNums;
    static OptionItem OptionProtectCooldown;
    static OptionItem OptionTargetCanSeeProtect;
    static OptionItem OptionKnowTargetShieldBroken;
    enum OptionName
    {
        MedicalerCooldown,
        MedicalerSkillLimit,
        MedicalerTargetCanSeeProtect,
        MedicalerKnowTargetShieldBroken,
    }

    private int ProtectLimit;
    private static List<byte> ProtectList;
    public bool IsKiller { get; private set; } = false;
    private static void SetupOptionItem()
    {
        OptionProtectNums = FloatOptionItem.Create(RoleInfo, 10, OptionName.MedicalerCooldown, new(2.5f, 180f, 2.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionProtectCooldown = IntegerOptionItem.Create(RoleInfo, 11, OptionName.MedicalerSkillLimit, new(1, 99, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionTargetCanSeeProtect = BooleanOptionItem.Create(RoleInfo, 12, OptionName.MedicalerTargetCanSeeProtect, true, false);
        OptionKnowTargetShieldBroken = BooleanOptionItem.Create(RoleInfo, 13, OptionName.MedicalerKnowTargetShieldBroken, true, false);
    }
    public override void Add()
    {
        var playerId = Player.PlayerId;
        ProtectLimit = OptionProtectNums.GetInt();

        if (!Main.ResetCamPlayerList.Contains(playerId))
            Main.ResetCamPlayerList.Add(playerId);
    }
    private void SendRPC_SyncLimit()
    {
        using var sender = CreateSender(CustomRPC.SetMedicalerProtectLimit);
        sender.Writer.Write(ProtectList.Count);
        for (int i = 0; i < ProtectList.Count; i++)
            sender.Writer.Write(ProtectList[i]);
    }
    private static void SendRPC_SyncList()
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetMedicalerProtectList, SendOption.Reliable, -1);
        writer.Write(ProtectList.Count);
        for (int i = 0; i < ProtectList.Count; i++)
            writer.Write(ProtectList[i]);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType == CustomRPC.SetMedicalerProtectLimit)
        {
            ProtectLimit = reader.ReadInt32();
        }
        else if (rpcType == CustomRPC.SetMedicalerProtectList)
        {
            ProtectList = new();
            for (int i = 0; i < reader.ReadInt32(); i++)
                ProtectList.Add(reader.ReadByte());
        }
    }
    public override bool OverrideAbilityButtonText(out string text)
    {
        text = Translator.GetString("MedicalerButtonText");
        return true;
    }
    public float CalculateKillCooldown() => CanUseKillButton() ? OptionProtectCooldown.GetFloat() : 255f;
    public bool CanUseKillButton()
       => Player.IsAlive()
       && ProtectLimit > 0;
    public override bool CanSabotage(SystemTypes systemType) => false;
    public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(false);
    public override string GetProgressText(bool comms = false) => Utils.ColorString(CanUseKillButton() ? RoleInfo.RoleColor : Color.gray, $"({ProtectLimit})");
    public static bool InProtect(byte id) => ProtectList.Contains(id) && !(PlayerState.GetByPlayerId(id)?.IsDead ?? true);
    public bool OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsSuicide) return true;
        var (killer, target) = info.AttemptTuple;
        if (ProtectList.Contains(target.PlayerId)) return false;

        ProtectLimit--;
        SendRPC_SyncLimit();
        ProtectList.Add(target.PlayerId);
        SendRPC_SyncList();

        killer.SetKillCooldownV2(target: target);
        killer.RPCPlayCustomSound("Shield");
        target.RPCPlayCustomSound("Shield");

        Utils.NotifyRoles(killer);
        Utils.NotifyRoles(target);

        Logger.Info($"{killer.GetNameWithRole()} : 将护盾发送给 {target.GetNameWithRole()}", "Medicaler.OnCheckMurderAsKiller");
        Logger.Info($"{killer.GetNameWithRole()} : 剩余{ProtectLimit}个护盾", "Medicaler.OnCheckMurderAsKiller");
        return false;
    }
    private static bool OnCheckMurderPlayerOthers_Before(MurderInfo info)
    {
        if (info.IsSuicide) return true;
        var (killer, target) = info.AttemptTuple;
        if (!ProtectList.Contains(target.PlayerId)) return true;

        ProtectList.Remove(target.PlayerId);
        SendRPC_SyncList();

        killer.SetKillCooldownV2(target: target, forceAnime: true);
        killer.RpcGuardAndKill(target);

        if (OptionTargetCanSeeProtect.GetBool())
            target.RpcGuardAndKill(target);

        if (OptionKnowTargetShieldBroken.GetBool())
            Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Medicaler)).Do(x => x.Notify(Translator.GetString("MedicalerTargetShieldBroken")));
        else
            Main.AllPlayerControls.Where(x => x.Is(CustomRoles.Medicaler)).Do(x => Utils.NotifyRoles(x));

        Logger.Info($"{target.GetNameWithRole()} : 来自医生的盾破碎", "Medicaler.OnCheckMurderPlayerOthers_Before");

        info.CanKill = false;

        return false;
    }
    public static string MarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        return (seer.Is(CustomRoles.Medicaler)
            || (seer.PlayerId == seen.PlayerId && OptionTargetCanSeeProtect.GetBool())
            ) && InProtect(seen.PlayerId) ? Utils.ColorString(RoleInfo.RoleColor, "●") : "";
    }
}